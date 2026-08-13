using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pvm.Application.Submissions;
using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Pvm.Infrastructure.Persistence.Repositories;
using Pvm.Infrastructure.PayloadArchive;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Persistence;

public sealed class SubmissionOperationPersistenceTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();
    private readonly IPayloadArchive _archive = new FileSystemPayloadArchive(
        Path.Combine(Path.GetTempPath(), $"pvm-payload-tests-{Guid.NewGuid():N}"),
        "payloads");

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Twenty_concurrent_commands_for_one_candidate_produce_one_external_post()
    {
        var candidateId = await ResetAndSeedCandidateAsync();
        var client = new CountingShopriteInvoiceClient(
            new ShopriteInvoiceResponse(true, 200, "accepted", IsAmbiguous: false),
            TimeSpan.FromMilliseconds(100));

        var results = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(async _ =>
            {
                await using var db = CreateDbContext();
                var handler = new SubmitShopriteInvoiceHandler(
                    new EfInvoiceCandidateRepository(db),
                    client,
                    _archive);
                return await handler.HandleAsync(
                    new SubmitShopriteInvoiceCommand(
                        Guid.NewGuid(),
                        candidateId,
                        "worker@test",
                        "automatic"),
                    CancellationToken.None);
            }));

        Assert.Equal(1, client.SubmitCallCount);
        Assert.Single(results, result => result.Status == SubmitShopriteInvoiceStatus.Submitted);
        Assert.All(
            results,
            result => Assert.Contains(
                result.Status,
                new[]
                {
                    SubmitShopriteInvoiceStatus.Submitted,
                    SubmitShopriteInvoiceStatus.InProgress,
                    SubmitShopriteInvoiceStatus.DuplicateBlocked
                }));

        await using var verificationDb = CreateDbContext();
        var operation = Assert.Single(await verificationDb.SubmissionOperations.ToListAsync());
        Assert.Equal("Submitted", operation.Status);
        Assert.NotNull(operation.SendingStartedAt);
        Assert.NotNull(operation.CompletedAt);
        Assert.False(string.IsNullOrWhiteSpace(operation.SourceVersion));
        Assert.Null(operation.FrozenSourceJson);
        Assert.Null(operation.FrozenCanonicalJson);
        Assert.Null(operation.RequestPayload);
        Assert.Null(operation.ResponsePayload);
        Assert.Equal(64, operation.RequestPayloadHash.Length);
        var payloads = await verificationDb.PayloadArchives
            .OrderBy(payload => payload.Kind)
            .ToArrayAsync();
        Assert.Equal(4, payloads.Length);
        Assert.All(payloads, payload => Assert.Equal(64, payload.Sha256Hash.Length));
        var reconstructed = new Dictionary<PayloadArchiveKind, string>();
        foreach (var payload in payloads)
        {
            var kind = Enum.Parse<PayloadArchiveKind>(payload.Kind);
            reconstructed[kind] = await _archive.ReadVerifiedAsync(
                new PayloadArchiveRecord(
                    kind,
                    payload.Location,
                    payload.Sha256Hash,
                    payload.ContentType,
                    payload.ByteCount,
                    payload.CreatedAt),
                CancellationToken.None);
        }

        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""{"source":"acumatica","version":1}"""),
            JsonNode.Parse(reconstructed[PayloadArchiveKind.AcumaticaSource])));
        Assert.Equal(
            "INV-1",
            JsonNode.Parse(reconstructed[PayloadArchiveKind.CanonicalInvoice])![
                "invoiceNumber"]!.GetValue<string>());
        Assert.Contains(
            "invoiceMessage",
            reconstructed[PayloadArchiveKind.ShopriteRequest]);
        Assert.Equal("accepted", reconstructed[PayloadArchiveKind.ShopriteResponse]);
        var attempt = Assert.Single(await verificationDb.InvoiceSubmissionAttempts.ToListAsync());
        Assert.Null(attempt.RequestPayload);
        Assert.Null(attempt.ResponsePayload);
        Assert.NotNull(attempt.RequestPayloadLocation);
        Assert.NotNull(attempt.ResponsePayloadLocation);
        var transitions = await verificationDb.SubmissionOperationTransitions
            .OrderBy(transition => transition.CreatedAt)
            .ToArrayAsync();
        Assert.Equal(
            new[] { "Pending", "Sending", "Submitted" },
            transitions.Select(transition => transition.NewState));
        Assert.Equal(
            new string?[] { null, "Pending", "Sending" },
            transitions.Select(transition => transition.PreviousState));
        Assert.All(transitions, transition =>
        {
            Assert.Equal("worker@test", transition.Actor);
            Assert.Equal("automatic", transition.Mode);
            Assert.Equal(operation.CorrelationId, transition.CorrelationId);
            Assert.Equal(operation.SourceVersion, transition.SourceVersion);
            Assert.Equal(operation.RequestPayloadHash, transition.PayloadHash);
            Assert.False(string.IsNullOrWhiteSpace(transition.Reason));
        });
    }

    [Fact]
    public async Task Rejected_operation_blocks_redelivery_and_new_ordinary_commands()
    {
        var candidateId = await ResetAndSeedCandidateAsync();
        var command = new SubmitShopriteInvoiceCommand(
            Guid.Parse("3b621803-736d-45ac-832b-923d99a19f42"),
            candidateId,
            "operator@test",
            "manual");
        var client = new CountingShopriteInvoiceClient(
            new ShopriteInvoiceResponse(false, 417, "rejected", IsAmbiguous: false));

        await using (var firstDb = CreateDbContext())
        {
            var handler = new SubmitShopriteInvoiceHandler(
                new EfInvoiceCandidateRepository(firstDb),
                client,
                _archive);
            var first = await handler.HandleAsync(command, CancellationToken.None);
            Assert.Equal(SubmitShopriteInvoiceStatus.Failed, first.Status);
        }

        await using (var redeliveryDb = CreateDbContext())
        {
            var handler = new SubmitShopriteInvoiceHandler(
                new EfInvoiceCandidateRepository(redeliveryDb),
                client,
                _archive);
            var redelivery = await handler.HandleAsync(command, CancellationToken.None);
            Assert.Equal(SubmitShopriteInvoiceStatus.Failed, redelivery.Status);
        }

        await using (var newCommandDb = CreateDbContext())
        {
            var handler = new SubmitShopriteInvoiceHandler(
                new EfInvoiceCandidateRepository(newCommandDb),
                client,
                _archive);
            var newCommand = await handler.HandleAsync(
                command with { CommandId = Guid.NewGuid() },
                CancellationToken.None);
            Assert.Equal(SubmitShopriteInvoiceStatus.Failed, newCommand.Status);
        }

        Assert.Equal(1, client.SubmitCallCount);
    }

    [Fact]
    public async Task Concurrent_commands_do_not_supersede_a_fast_rejection()
    {
        var candidateId = await ResetAndSeedCandidateAsync();
        var client = new CountingShopriteInvoiceClient(
            new ShopriteInvoiceResponse(false, 417, "rejected", IsAmbiguous: false),
            TimeSpan.FromMilliseconds(50));

        var results = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(async _ =>
            {
                await using var db = CreateDbContext();
                var handler = new SubmitShopriteInvoiceHandler(
                    new EfInvoiceCandidateRepository(db),
                    client,
                    _archive);
                return await handler.HandleAsync(
                    new SubmitShopriteInvoiceCommand(
                        Guid.NewGuid(),
                        candidateId,
                        "worker@test",
                        "automatic"),
                    CancellationToken.None);
            }));

        Assert.Equal(1, client.SubmitCallCount);
        Assert.All(
            results,
            result => Assert.Contains(
                result.Status,
                new[]
                {
                    SubmitShopriteInvoiceStatus.Failed,
                    SubmitShopriteInvoiceStatus.InProgress
                }));
    }

    [Fact]
    public async Task Stale_sending_operation_becomes_ambiguous_and_blocks_redelivery()
    {
        var candidateId = await ResetAndSeedCandidateAsync();
        var commandId = Guid.Parse("5e33ef28-d8e2-4ccf-8368-c246645f5786");
        Guid operationId;

        await using (var preparationDb = CreateDbContext())
        {
            var repository = new EfInvoiceCandidateRepository(preparationDb);
            var snapshot = Assert.IsType<InvoiceSubmissionSnapshot>(
                await repository.GetSubmissionSnapshotAsync(candidateId, CancellationToken.None));
            var operation = await repository.GetOrCreateSubmissionOperationAsync(
                new PrepareSubmissionOperation(
                    candidateId,
                    commandId,
                    "worker@test",
                    "automatic",
                    snapshot.IdempotencyKey,
                    snapshot.SourceVersion,
                    snapshot.SourceJson,
                    snapshot.CanonicalJson,
                    "<invoice />"),
                CancellationToken.None);
            operationId = operation.Id;
            await ArchivePreparedOperationAsync(repository, operation);
            Assert.True(await repository.TryStartSubmissionOperationAsync(
                operation.Id,
                DateTimeOffset.UtcNow,
                expectedAutomationPolicyVersion: null,
                automatic: true,
                cancellationToken: CancellationToken.None));
            await preparationDb.SubmissionOperations
                .Where(item => item.Id == operation.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        item => item.SendingStartedAt,
                        DateTimeOffset.UtcNow.AddHours(-1)));
        }

        var client = new CountingShopriteInvoiceClient(
            new ShopriteInvoiceResponse(true, 200, "accepted", IsAmbiguous: false));
        await using (var redeliveryDb = CreateDbContext())
        {
            var handler = new SubmitShopriteInvoiceHandler(
                new EfInvoiceCandidateRepository(redeliveryDb),
                client,
                _archive);
            var result = await handler.HandleAsync(
                new SubmitShopriteInvoiceCommand(commandId, candidateId, "worker@test", "automatic"),
                CancellationToken.None);
            Assert.Equal(SubmitShopriteInvoiceStatus.ManualReviewRequired, result.Status);
            Assert.Equal(operationId, result.SubmissionOperationId);
        }

        Assert.Equal(0, client.SubmitCallCount);
        await using var verificationDb = CreateDbContext();
        Assert.Equal(
            new[] { "Pending", "Sending", "Ambiguous" },
            await verificationDb.SubmissionOperationTransitions
                .Where(transition => transition.SubmissionOperationId == operationId)
                .OrderBy(transition => transition.CreatedAt)
                .Select(transition => transition.NewState)
                .ToArrayAsync());
    }

    [Fact]
    public async Task Recovery_racing_with_completion_keeps_candidate_and_operation_consistent()
    {
        var candidateId = await ResetAndSeedCandidateAsync();
        Guid operationId;

        await using (var preparationDb = CreateDbContext())
        {
            var repository = new EfInvoiceCandidateRepository(preparationDb);
            var snapshot = Assert.IsType<InvoiceSubmissionSnapshot>(
                await repository.GetSubmissionSnapshotAsync(candidateId, CancellationToken.None));
            var operation = await repository.GetOrCreateSubmissionOperationAsync(
                new PrepareSubmissionOperation(
                    candidateId,
                    Guid.NewGuid(),
                    "worker@test",
                    "automatic",
                    snapshot.IdempotencyKey,
                    snapshot.SourceVersion,
                    snapshot.SourceJson,
                    snapshot.CanonicalJson,
                    "<invoice />"),
                CancellationToken.None);
            operationId = operation.Id;
            await ArchivePreparedOperationAsync(repository, operation);
            Assert.True(await repository.TryStartSubmissionOperationAsync(
                operation.Id,
                DateTimeOffset.UtcNow,
                expectedAutomationPolicyVersion: null,
                automatic: true,
                cancellationToken: CancellationToken.None));
            await preparationDb.SubmissionOperations
                .Where(item => item.Id == operation.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        item => item.SendingStartedAt,
                        DateTimeOffset.UtcNow.AddHours(-1)));
        }

        var completion = Task.Run(async () =>
        {
            await using var db = CreateDbContext();
            var response = new ShopriteInvoiceResponse(true, 200, "accepted", IsAmbiguous: false);
            var responseArchive = await _archive.WriteAsync(
                new PayloadArchiveWrite(
                    PayloadArchiveKind.ShopriteResponse,
                    $"shoprite/invoices/2026/07/{operationId:D}/response.txt",
                    "text/plain; charset=utf-8",
                    response.Body),
                CancellationToken.None);
            await new EfInvoiceCandidateRepository(db).CompleteSubmissionOperationAsync(
                operationId,
                response,
                responseArchive,
                CancellationToken.None);
        });
        var recovery = Task.Run(async () =>
        {
            await using var db = CreateDbContext();
            await new EfInvoiceCandidateRepository(db).MarkStaleSendingOperationsAmbiguousAsync(
                DateTimeOffset.UtcNow.AddMinutes(-15),
                DateTimeOffset.UtcNow,
                CancellationToken.None);
        });

        await Task.WhenAll(completion, recovery);

        await using var verificationDb = CreateDbContext();
        var operationStatus = await verificationDb.SubmissionOperations
            .Where(operation => operation.Id == operationId)
            .Select(operation => operation.Status)
            .SingleAsync();
        var candidateStatus = await verificationDb.InvoiceCandidates
            .Where(candidate => candidate.Id == candidateId)
            .Select(candidate => candidate.Status)
            .SingleAsync();
        Assert.Contains(operationStatus, new[] { "Submitted", "Ambiguous" });
        Assert.Equal(operationStatus, candidateStatus);
    }

    [Fact]
    public async Task Persisted_transition_history_cannot_be_edited()
    {
        var candidateId = await ResetAndSeedCandidateAsync();
        var client = new CountingShopriteInvoiceClient(
            new ShopriteInvoiceResponse(true, 200, "accepted", IsAmbiguous: false));
        await using (var submissionDb = CreateDbContext())
        {
            var handler = new SubmitShopriteInvoiceHandler(
                new EfInvoiceCandidateRepository(submissionDb),
                client,
                _archive);
            var result = await handler.HandleAsync(
                new SubmitShopriteInvoiceCommand(
                    Guid.NewGuid(),
                    candidateId,
                    "operator@test",
                    "manual"),
                CancellationToken.None);
            Assert.Equal(SubmitShopriteInvoiceStatus.Submitted, result.Status);
        }

        await using var tamperDb = CreateDbContext();
        var transition = await tamperDb.SubmissionOperationTransitions.FirstAsync();
        transition.Reason = "edited";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => tamperDb.SaveChangesAsync());
        var updateException = Assert.IsType<DbUpdateException>(exception.InnerException);
        var databaseException = Assert.IsType<PostgresException>(updateException.InnerException);
        Assert.Equal("55000", databaseException.SqlState);
    }

    [Fact]
    public async Task Response_archive_failure_after_send_is_persisted_as_ambiguous()
    {
        var candidateId = await ResetAndSeedCandidateAsync();
        var client = new CountingShopriteInvoiceClient(
            new ShopriteInvoiceResponse(true, 200, "accepted", IsAmbiguous: false));
        await using (var submissionDb = CreateDbContext())
        {
            var handler = new SubmitShopriteInvoiceHandler(
                new EfInvoiceCandidateRepository(submissionDb),
                client,
                new FailingResponseArchive(_archive));
            var result = await handler.HandleAsync(
                new SubmitShopriteInvoiceCommand(
                    Guid.NewGuid(),
                    candidateId,
                    "operator@test",
                    "manual"),
                CancellationToken.None);
            Assert.Equal(SubmitShopriteInvoiceStatus.Ambiguous, result.Status);
        }

        await using var verificationDb = CreateDbContext();
        var operation = Assert.Single(await verificationDb.SubmissionOperations.ToListAsync());
        Assert.Equal("Ambiguous", operation.Status);
        Assert.Equal("response-archive-failed", operation.FailureClassification);
        Assert.Null(operation.ResponsePayload);
        var attempt = Assert.Single(await verificationDb.InvoiceSubmissionAttempts.ToListAsync());
        Assert.Equal("response-archive-failed", attempt.FailureClassification);
        Assert.False(attempt.IsRetryEligible);
        Assert.Equal(
            new[] { "Pending", "Sending", "Ambiguous" },
            await verificationDb.SubmissionOperationTransitions
                .OrderBy(transition => transition.CreatedAt)
                .Select(transition => transition.NewState)
                .ToArrayAsync());
    }

    private async Task ArchivePreparedOperationAsync(
        EfInvoiceCandidateRepository repository,
        SubmissionOperation operation)
    {
        var source = Assert.IsType<string>(operation.FrozenSourceJson);
        var canonical = Assert.IsType<string>(operation.FrozenCanonicalJson);
        var request = Assert.IsType<string>(operation.RequestPayload);
        var payloads = new[]
        {
            await _archive.WriteAsync(
                new PayloadArchiveWrite(
                    PayloadArchiveKind.AcumaticaSource,
                    $"acumatica/invoices/2026/07/{operation.InvoiceCandidateId:D}/{operation.SourceVersion}/source.json",
                    "application/json",
                    source),
                CancellationToken.None),
            await _archive.WriteAsync(
                new PayloadArchiveWrite(
                    PayloadArchiveKind.CanonicalInvoice,
                    $"acumatica/invoices/2026/07/{operation.InvoiceCandidateId:D}/{operation.SourceVersion}/canonical.json",
                    "application/json",
                    canonical),
                CancellationToken.None),
            await _archive.WriteAsync(
                new PayloadArchiveWrite(
                    PayloadArchiveKind.ShopriteRequest,
                    $"shoprite/invoices/2026/07/{operation.Id:D}/request.xml",
                    "application/xml",
                    request),
                CancellationToken.None)
        };
        await repository.RecordPreparedPayloadArchivesAsync(
            operation.Id,
            payloads,
            CancellationToken.None);
    }

    private async Task<Guid> ResetAndSeedCandidateAsync()
    {
        await using var db = CreateDbContext();
        await DatabaseMigrationRunner.MigrateAsync(db);

        var purchaseOrder = new ShopritePurchaseOrderEntity
        {
            Id = Guid.NewGuid(),
            PurchaseOrderNumber = "PO-123",
            DeliveryLocationSource = "buyer",
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow
        };
        var invoice = ValidInvoice();
        var candidate = new InvoiceCandidateEntity
        {
            Id = Guid.NewGuid(),
            AcumaticaInvoiceId = invoice.AcumaticaInvoiceId,
            InvoiceNumber = invoice.InvoiceNumber,
            CustomerAccount = invoice.CustomerAccount,
            ShopritePurchaseOrderNumber = purchaseOrder.PurchaseOrderNumber,
            MatchedShopritePurchaseOrderId = purchaseOrder.Id,
            IdempotencyKey = $"invoice:{invoice.InvoiceNumber}",
            Status = "Ready",
            SourceJson = """{"source":"acumatica","version":1}""",
            CanonicalJson = JsonSerializer.Serialize(invoice, SerializerOptions),
            ValidationJson = JsonSerializer.Serialize(new ValidationResult([]), SerializerOptions),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.ShopritePurchaseOrders.Add(purchaseOrder);
        db.InvoiceCandidates.Add(candidate);
        await db.SaveChangesAsync();
        return candidate.Id;
    }

    private PvmDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PvmDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new PvmDbContext(options);
    }

    private static CanonicalInvoice ValidInvoice()
        => new(
            AcumaticaInvoiceId: "ACU-INV-1",
            InvoiceNumber: "INV-1",
            CustomerAccount: "SHOPRITE",
            CustomerLocation: "DC-01",
            ShopritePurchaseOrderNumber: "PO-123",
            SupplierGln: "6001197000006",
            SellerVatRegistrationNumber: "4010137059",
            StoreDcGln: "6001001018104",
            CountryCode: "ZA",
            CurrencyCode: "ZAR",
            InvoiceDate: new DateTimeOffset(2026, 7, 28, 10, 0, 0, TimeSpan.Zero),
            TotalExcludingTax: new Money("ZAR", 100m),
            TotalIncludingTax: new Money("ZAR", 115m),
            TotalTax: new Money("ZAR", 15m),
            Lines:
            [
                new CanonicalInvoiceLine(
                    1,
                    "SKU-1",
                    "06001197181125",
                    "Test item",
                    1m,
                    "EA",
                    ShopriteMeasurementUnit.EA,
                    1m,
                    new Money("ZAR", 100m),
                    new Money("ZAR", 115m),
                    new Money("ZAR", 15m),
                    "STANDARD",
                    15m,
                    false)
            ]);

    private sealed class CountingShopriteInvoiceClient(
        ShopriteInvoiceResponse response,
        TimeSpan? delay = null) : IShopriteInvoiceClient
    {
        private int _submitCallCount;

        public int SubmitCallCount => _submitCallCount;

        public async Task<ShopriteInvoiceResponse> SubmitAsync(
            string xml,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _submitCallCount);
            if (delay is not null)
            {
                await Task.Delay(delay.Value, cancellationToken);
            }

            return response;
        }
    }

    private sealed class FailingResponseArchive(IPayloadArchive inner) : IPayloadArchive
    {
        public Task<PayloadArchiveRecord> WriteAsync(
            PayloadArchiveWrite payload,
            CancellationToken cancellationToken)
            => payload.Kind == PayloadArchiveKind.ShopriteResponse
                ? throw new IOException("response archive unavailable")
                : inner.WriteAsync(payload, cancellationToken);

        public Task<string> ReadVerifiedAsync(
            PayloadArchiveRecord payload,
            CancellationToken cancellationToken)
            => inner.ReadVerifiedAsync(payload, cancellationToken);
    }
}
