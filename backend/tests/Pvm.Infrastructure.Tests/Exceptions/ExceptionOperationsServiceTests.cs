using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pvm.Application.Exceptions;
using Pvm.Application.Messaging;
using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;
using Pvm.Infrastructure.Exceptions;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Pvm.Infrastructure.Shoprite;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Exceptions;

public sealed class ExceptionOperationsServiceTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Synchronization_creates_one_task_for_each_condition_and_does_not_duplicate_it()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var service = Service(db);
        var candidate = await SeedCandidateAsync(db, status: "Ready");
        await SeedOperationAsync(db, candidate.Id, "Ambiguous");
        var now = DateTimeOffset.UtcNow;

        await service.SynchronizeAsync(now, CancellationToken.None);
        await service.SynchronizeAsync(now.AddMinutes(5), CancellationToken.None);

        var task = Assert.Single(await db.ExceptionTasks.ToListAsync());
        Assert.Equal(ExceptionCategories.AmbiguousSubmission, task.Category);
        Assert.Equal(ExceptionSeverities.Critical, task.Severity);
        Assert.Equal(ExceptionTaskStatuses.Open, task.Status);
        Assert.Equal(ExceptionRetryClassifications.ManualResolutionRequired, task.RetryClassification);
        Assert.Equal(candidate.Id, task.InvoiceCandidateId);
        Assert.Equal(1, task.OccurrenceCount);
    }

    [Fact]
    public async Task Synchronization_resolves_a_task_whose_condition_disappeared()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var service = Service(db);
        var candidate = await SeedCandidateAsync(db, status: "Ready");
        var operation = await SeedOperationAsync(db, candidate.Id, "Ambiguous");
        await service.SynchronizeAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        operation.Status = "Submitted";
        await db.SaveChangesAsync();
        await service.SynchronizeAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        var task = Assert.Single(await db.ExceptionTasks.ToListAsync());
        Assert.Equal(ExceptionTaskStatuses.Resolved, task.Status);
        Assert.Equal("system:synchronization", task.ResolvedBy);
    }

    [Fact]
    public async Task Ambiguous_resolution_requires_evidence_before_it_changes_any_state()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var service = Service(db);
        var candidate = await SeedCandidateAsync(db, status: "Ready");
        var operation = await SeedOperationAsync(db, candidate.Id, "Ambiguous");

        var result = await service.ResolveAmbiguousAsync(
            operation.Id,
            AmbiguousResolutionOutcome.ConfirmedAccepted,
            evidence: "   ",
            reason: "Checked the Shoprite portal.",
            actor: "admin@pvm.co.za",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Equal("Ambiguous", (await db.SubmissionOperations.SingleAsync()).Status);
        Assert.Empty(await db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task Confirmed_acceptance_marks_the_operation_submitted_and_records_the_evidence()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var service = Service(db);
        var candidate = await SeedCandidateAsync(db, status: "Ready");
        var operation = await SeedOperationAsync(db, candidate.Id, "Ambiguous");
        await service.SynchronizeAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        var result = await service.ResolveAmbiguousAsync(
            operation.Id,
            AmbiguousResolutionOutcome.ConfirmedAccepted,
            evidence: "Shoprite support confirmed receipt reference 4471.",
            reason: "Confirmed by Shoprite support.",
            actor: "admin@pvm.co.za",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Equal("Submitted", (await db.SubmissionOperations.SingleAsync()).Status);
        Assert.Equal("Submitted", (await db.InvoiceCandidates.SingleAsync()).Status);
        var transition = await db.SubmissionOperationTransitions
            .SingleAsync(item => item.NewState == "Submitted");
        Assert.Equal("Ambiguous", transition.PreviousState);
        Assert.Equal("admin@pvm.co.za", transition.Actor);
        var audit = await db.AuditEvents.SingleAsync(item => item.Action == "ambiguous-submission-resolved");
        Assert.Contains("4471", audit.DetailsJson);
        Assert.Equal(ExceptionTaskStatuses.Resolved, (await db.ExceptionTasks.SingleAsync()).Status);
    }

    [Fact]
    public async Task An_unknown_outcome_records_evidence_without_changing_the_submission()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var service = Service(db);
        var candidate = await SeedCandidateAsync(db, status: "Ready");
        var operation = await SeedOperationAsync(db, candidate.Id, "Ambiguous");
        await service.SynchronizeAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        var result = await service.ResolveAmbiguousAsync(
            operation.Id,
            AmbiguousResolutionOutcome.StillUnknown,
            evidence: "Shoprite has not answered the query yet.",
            reason: "Waiting for Shoprite.",
            actor: "admin@pvm.co.za",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Equal("Ambiguous", (await db.SubmissionOperations.SingleAsync()).Status);
        Assert.Equal("Ready", (await db.InvoiceCandidates.SingleAsync()).Status);
        var task = await db.ExceptionTasks.SingleAsync();
        Assert.Equal(ExceptionTaskStatuses.WaitingForShoprite, task.Status);
        Assert.Single(await db.ExceptionTaskComments.ToListAsync());
    }

    [Fact]
    public async Task An_ambiguous_task_cannot_be_closed_without_a_recorded_outcome()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var service = Service(db);
        var candidate = await SeedCandidateAsync(db, status: "Ready");
        await SeedOperationAsync(db, candidate.Id, "Ambiguous");
        await service.SynchronizeAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        var task = await db.ExceptionTasks.SingleAsync();

        var result = await service.SetStatusAsync(
            task.Id,
            ExceptionTaskStatuses.Resolved,
            "admin@pvm.co.za",
            "Looks fine to me.",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Equal(ExceptionTaskStatuses.Open, (await db.ExceptionTasks.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_hold_suspends_the_invoice_and_a_release_revalidates_it()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var service = Service(db);
        var candidate = await SeedCandidateAsync(db, status: "Ready");

        var held = await service.HoldInvoiceAsync(
            candidate.Id,
            "Price variance under investigation.",
            "admin@pvm.co.za",
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        var suspendedStatus = (await db.InvoiceCandidates.SingleAsync()).Status;
        var released = await service.ReleaseInvoiceAsync(
            candidate.Id,
            "Variance accepted.",
            "admin@pvm.co.za",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.True(held.Applied);
        Assert.Equal("Suspended", suspendedStatus);
        Assert.True(released.Applied);
        Assert.Equal("Ready", (await db.InvoiceCandidates.SingleAsync()).Status);
        Assert.Contains(await db.AuditEvents.ToListAsync(), audit => audit.Action == "invoice-hold-applied");
        Assert.Contains(await db.AuditEvents.ToListAsync(), audit => audit.Action == "invoice-hold-released");
    }

    [Fact]
    public async Task A_submitted_invoice_cannot_be_held()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var service = Service(db);
        var candidate = await SeedCandidateAsync(db, status: "Submitted");

        var result = await service.HoldInvoiceAsync(
            candidate.Id,
            "Late correction.",
            "admin@pvm.co.za",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.False(result.Applied);
        Assert.True(result.Conflict);
        Assert.Equal("Submitted", (await db.InvoiceCandidates.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_retry_is_refused_while_the_submission_outcome_is_ambiguous()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var service = Service(db);
        var candidate = await SeedCandidateAsync(db, status: "Ready");
        await SeedOperationAsync(db, candidate.Id, "Ambiguous");

        var result = await service.RetryRejectedAsync(
            candidate.Id,
            "Try again.",
            "admin@pvm.co.za",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.False(result.Applied);
        Assert.True(result.Conflict);
        Assert.Equal("Ambiguous", (await db.SubmissionOperations.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_retry_retires_the_rejected_operation_only_after_validation_passes()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var service = Service(db);
        var candidate = await SeedCandidateAsync(db, status: "Rejected");
        var operation = await SeedOperationAsync(db, candidate.Id, "Rejected");

        var result = await service.RetryRejectedAsync(
            candidate.Id,
            "Mapping corrected.",
            "admin@pvm.co.za",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Equal("Ready", (await db.InvoiceCandidates.SingleAsync()).Status);
        Assert.Equal("Cancelled", (await db.SubmissionOperations.SingleAsync(item => item.Id == operation.Id)).Status);
        Assert.Contains(
            await db.AuditEvents.ToListAsync(),
            audit => audit.Action == "submission-retry-authorized");
    }

    [Fact]
    public async Task A_retry_is_refused_and_audited_when_the_invoice_still_fails_validation()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var service = Service(db);
        var candidate = await SeedCandidateAsync(db, status: "Rejected", withPurchaseOrder: false);
        var operation = await SeedOperationAsync(db, candidate.Id, "Rejected");

        var result = await service.RetryRejectedAsync(
            candidate.Id,
            "Try again without fixing anything.",
            "admin@pvm.co.za",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Equal("NeedsReview", (await db.InvoiceCandidates.SingleAsync()).Status);
        Assert.Equal("Rejected", (await db.SubmissionOperations.SingleAsync(item => item.Id == operation.Id)).Status);
        Assert.Contains(await db.AuditEvents.ToListAsync(), audit => audit.Action == "submission-retry-refused");
    }

    [Fact]
    public async Task A_dead_lettered_submission_cannot_be_replayed_when_a_send_may_have_reached_shoprite()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var service = Service(db);
        var candidate = await SeedCandidateAsync(db, status: "Ready");
        await SeedOperationAsync(db, candidate.Id, "Ambiguous");
        var delivery = await SeedDeadLetterAsync(
            db,
            IntegrationQueues.ShopriteInvoiceSubmit,
            IntegrationMessageTypes.ShopriteInvoiceSubmitV1,
            candidate.Id);

        var result = await service.ReplayDeadLetterAsync(
            delivery.Id,
            "Please replay.",
            "admin@pvm.co.za",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.False(result.Applied);
        Assert.True(result.Conflict);
        Assert.Single(await db.IntegrationOutboxMessages.ToListAsync());
        Assert.Equal("DeadLettered", (await db.IntegrationMessageDeliveries.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_safe_dead_letter_replay_creates_a_new_message_identity()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var service = Service(db);
        var delivery = await SeedDeadLetterAsync(
            db,
            IntegrationQueues.ShopritePurchaseOrderRefresh,
            IntegrationMessageTypes.ShopritePurchaseOrderRefreshV1,
            invoiceCandidateId: null);

        var result = await service.ReplayDeadLetterAsync(
            delivery.Id,
            "Service Bus outage cleared.",
            "admin@pvm.co.za",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.True(result.Applied);
        var messages = await db.IntegrationOutboxMessages.OrderBy(item => item.CreatedAt).ToListAsync();
        Assert.Equal(2, messages.Count);
        var replay = messages.Single(item => item.Id != Guid.Parse(delivery.MessageId));
        Assert.Equal(delivery.MessageId, replay.CausationId);
        using var envelope = JsonDocument.Parse(replay.PayloadJson);
        Assert.Equal(replay.Id.ToString("D"), envelope.RootElement.GetProperty("messageId").GetString());
        Assert.Equal(delivery.MessageId, envelope.RootElement.GetProperty("causationId").GetString());
        Assert.Equal("Replayed", (await db.IntegrationMessageDeliveries.SingleAsync()).Status);
        Assert.Contains(await db.AuditEvents.ToListAsync(), audit => audit.Action == "dead-letter-replayed");
    }

    [Fact]
    public async Task A_dead_letter_task_reports_whether_replay_is_safe()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var service = Service(db);
        await SeedDeadLetterAsync(
            db,
            IntegrationQueues.ShopritePurchaseOrderRefresh,
            IntegrationMessageTypes.ShopritePurchaseOrderRefreshV1,
            invoiceCandidateId: null);

        await service.SynchronizeAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        var listing = await service.ListAsync(
            status: "active",
            category: ExceptionCategories.DeadLetter,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        var task = Assert.Single(listing.Tasks);
        Assert.Equal(ExceptionRetryClassifications.SafeToReplay, task.RetryClassification);
        Assert.Equal(1, listing.Summary.DeadLetters);
    }

    [Fact]
    public async Task Historical_dead_letters_are_resolved_in_one_audited_action()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var service = Service(db);
        await SeedDeadLetterAsync(
            db,
            IntegrationQueues.ShopritePurchaseOrderRefresh,
            IntegrationMessageTypes.ShopritePurchaseOrderRefreshV1,
            invoiceCandidateId: null,
            receivedAt: DateTimeOffset.UtcNow.AddDays(-12));
        await service.SynchronizeAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        var result = await service.ResolveDeadLettersAsync(
            IntegrationQueues.ShopritePurchaseOrderRefresh,
            olderThanDays: 1,
            reason: "Parser defect fixed by PR #14.",
            actor: "admin@pvm.co.za",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Equal("DeadLetterResolved", (await db.IntegrationMessageDeliveries.SingleAsync()).Status);
        var task = await db.ExceptionTasks.SingleAsync();
        Assert.Equal(ExceptionTaskStatuses.Resolved, task.Status);
        Assert.Equal("admin@pvm.co.za", task.ResolvedBy);
        var audit = await db.AuditEvents.SingleAsync(item => item.Action == "dead-letters-resolved");
        Assert.Contains("PR #14", audit.DetailsJson);
    }

    [Fact]
    public async Task A_resolved_dead_letter_is_not_derived_again()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var service = Service(db);
        await SeedDeadLetterAsync(
            db,
            IntegrationQueues.ShopritePurchaseOrderRefresh,
            IntegrationMessageTypes.ShopritePurchaseOrderRefreshV1,
            invoiceCandidateId: null,
            receivedAt: DateTimeOffset.UtcNow.AddDays(-12));
        await service.SynchronizeAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        await service.ResolveDeadLettersAsync(
            null,
            olderThanDays: 1,
            reason: "Incident closed.",
            actor: "admin@pvm.co.za",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        await service.SynchronizeAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        var listing = await service.ListAsync("active", null, DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.Empty(listing.Tasks);
        Assert.Equal(0, listing.Summary.DeadLetters);
        Assert.Equal(ExceptionTaskStatuses.Resolved, (await db.ExceptionTasks.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_recent_dead_letter_is_kept_when_an_age_limit_is_given()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var service = Service(db);
        await SeedDeadLetterAsync(
            db,
            IntegrationQueues.ShopritePurchaseOrderRefresh,
            IntegrationMessageTypes.ShopritePurchaseOrderRefreshV1,
            invoiceCandidateId: null,
            receivedAt: DateTimeOffset.UtcNow.AddMinutes(-30));

        var result = await service.ResolveDeadLettersAsync(
            null,
            olderThanDays: 1,
            reason: "Bulk close.",
            actor: "admin@pvm.co.za",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Equal("DeadLettered", (await db.IntegrationMessageDeliveries.SingleAsync()).Status);
    }

    private static ExceptionOperationsService Service(PvmDbContext db)
        => new(db, new ShopriteInvoiceCandidateMatcher(db));

    private static async Task<InvoiceCandidateEntity> SeedCandidateAsync(
        PvmDbContext db,
        string status,
        bool withPurchaseOrder = true,
        string suffix = "1")
    {
        var now = DateTimeOffset.UtcNow;
        var purchaseOrderNumber = $"121538291{suffix}";
        Guid? purchaseOrderId = null;
        if (withPurchaseOrder)
        {
            var purchaseOrder = new ShopritePurchaseOrderEntity
            {
                Id = Guid.NewGuid(),
                PurchaseOrderNumber = purchaseOrderNumber,
                OrderTypeCode = "220",
                SupplierGln = "6001197000006",
                DeliveryGln = "6001001305600",
                DeliveryLocationCode = "30562",
                DeliveryLocationName = "CHECKERS LORRAINE",
                DeliveryLocationSource = "buyer",
                FirstSeenAt = now,
                LastSeenAt = now
            };
            db.ShopritePurchaseOrders.Add(purchaseOrder);
            purchaseOrderId = purchaseOrder.Id;
        }

        var invoice = new CanonicalInvoice(
            $"acu-invoice-{suffix}",
            $"INV15888{suffix}",
            "1810",
            "30562",
            purchaseOrderNumber,
            "6001197000006",
            "4010137059",
            "6001001305600",
            "ZA",
            "ZAR",
            now,
            new Money("ZAR", 100m),
            new Money("ZAR", 115m),
            new Money("ZAR", 15m),
            [new CanonicalInvoiceLine(
                1,
                "ENER10",
                "06001197181125",
                "Energy product",
                1m,
                "BOX",
                ShopriteMeasurementUnit.EA,
                null,
                new Money("ZAR", 100m),
                new Money("ZAR", 115m),
                new Money("ZAR", 15m),
                "STANDARD",
                15m,
                false)]);
        var candidate = new InvoiceCandidateEntity
        {
            Id = Guid.NewGuid(),
            AcumaticaInvoiceId = invoice.AcumaticaInvoiceId,
            InvoiceNumber = invoice.InvoiceNumber,
            CustomerAccount = invoice.CustomerAccount,
            CustomerLocation = invoice.CustomerLocation,
            ShopritePurchaseOrderNumber = invoice.ShopritePurchaseOrderNumber,
            MatchedShopritePurchaseOrderId = purchaseOrderId,
            SupplierGln = invoice.SupplierGln,
            StoreDcGln = invoice.StoreDcGln,
            IdempotencyKey = $"exception-test-{suffix}-{Guid.NewGuid():N}",
            Status = status,
            SourceJson = null,
            SourceLastModifiedAt = now,
            CanonicalJson = JsonSerializer.Serialize(invoice, SerializerOptions),
            ValidationJson = JsonSerializer.Serialize(new ValidationResult([]), SerializerOptions),
            CreatedAt = now.AddHours(-1),
            UpdatedAt = now.AddHours(-1)
        };
        db.InvoiceCandidates.Add(candidate);
        await db.SaveChangesAsync();
        return candidate;
    }

    private static async Task<SubmissionOperationEntity> SeedOperationAsync(
        PvmDbContext db,
        Guid invoiceCandidateId,
        string status)
    {
        var now = DateTimeOffset.UtcNow;
        var operation = new SubmissionOperationEntity
        {
            Id = Guid.NewGuid(),
            InvoiceCandidateId = invoiceCandidateId,
            CommandId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            Generation = 1,
            IdempotencyKey = $"operation-{Guid.NewGuid():N}",
            Status = status,
            InitiatedBy = "admin@pvm.co.za",
            InitiationMode = "manual",
            SourceVersion = "source-version",
            RequestPayloadHash = "hash",
            ResponseStatusCode = status == "Rejected" ? 400 : null,
            ErrorMessage = status == "Rejected" ? "Shoprite rejected the GTIN." : null,
            FailureClassification = status == "Rejected" ? "shoprite-validation" : null,
            CreatedAt = now.AddMinutes(-30),
            SendingStartedAt = now.AddMinutes(-29)
        };
        db.SubmissionOperations.Add(operation);
        await db.SaveChangesAsync();
        return operation;
    }

    private static async Task<IntegrationMessageDeliveryEntity> SeedDeadLetterAsync(
        PvmDbContext db,
        string queueName,
        string messageType,
        Guid? invoiceCandidateId,
        DateTimeOffset? receivedAt = null)
    {
        var now = receivedAt ?? DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();
        object data = invoiceCandidateId is null
            ? new { requestedBy = "system:test", trigger = IntegrationRunTriggers.Scheduled }
            : new SubmitShopriteInvoiceMessage(invoiceCandidateId.Value, Guid.NewGuid(), "system:test");
        var envelope = new IntegrationMessageEnvelope(
            messageId,
            messageType,
            Guid.NewGuid().ToString("D"),
            null,
            now.AddMinutes(-20),
            JsonSerializer.SerializeToElement(data, SerializerOptions));
        db.IntegrationOutboxMessages.Add(new IntegrationOutboxMessageEntity
        {
            Id = messageId,
            QueueName = queueName,
            MessageType = messageType,
            PayloadJson = JsonSerializer.Serialize(envelope, SerializerOptions),
            CorrelationId = envelope.CorrelationId,
            Status = "Published",
            AvailableAt = now.AddMinutes(-20),
            CreatedAt = now.AddMinutes(-20),
            UpdatedAt = now.AddMinutes(-20),
            PublishedAt = now.AddMinutes(-20)
        });
        var delivery = new IntegrationMessageDeliveryEntity
        {
            Id = Guid.NewGuid(),
            QueueName = queueName,
            MessageId = messageId.ToString("D"),
            MessageType = messageType,
            CorrelationId = envelope.CorrelationId,
            Status = "DeadLettered",
            DeliveryCount = 10,
            ErrorCode = "max-delivery-count",
            ErrorSummary = "The handler failed repeatedly.",
            DeadLetterReason = "max-delivery-count",
            EnqueuedAt = now.AddMinutes(-20),
            FirstReceivedAt = now.AddMinutes(-19),
            LastReceivedAt = now.AddMinutes(-10),
            UpdatedAt = now.AddMinutes(-10),
            CompletedAt = now.AddMinutes(-10)
        };
        db.IntegrationMessageDeliveries.Add(delivery);
        await db.SaveChangesAsync();
        return delivery;
    }

    private PvmDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PvmDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new PvmDbContext(options);
    }
}
