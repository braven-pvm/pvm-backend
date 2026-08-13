using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pvm.Application.Automation;
using Pvm.Application.Messaging;
using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;
using Pvm.Infrastructure.Automation;
using Pvm.Infrastructure.Messaging;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Automation;

public sealed class AutomationPolicyServiceTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Initial_policy_is_disabled_and_blocks_automatic_but_not_manual_submission()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var service = Service(db);
        var candidate = await SeedEligibleCandidateAsync(db);

        var policy = await service.GetCurrentAsync(CancellationToken.None);
        var automatic = await service.EvaluateSubmissionAsync(
            candidate.Id, "automatic", DateTimeOffset.UtcNow, CancellationToken.None);
        var manual = await service.EvaluateSubmissionAsync(
            candidate.Id, "manual", DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(AutomationMode.Disabled, policy.Mode);
        Assert.False(automatic.Allowed);
        Assert.Equal("automation-mode-blocked", automatic.Code);
        Assert.True(manual.Allowed);
    }

    [Fact]
    public async Task Shadow_records_would_submit_without_creating_outbox_message()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var service = Service(db);
        var candidate = await SeedEligibleCandidateAsync(db);
        var current = await service.GetCurrentAsync(CancellationToken.None);
        await ChangePolicyAsync(service, current.Version, AutomationMode.Shadow);

        var result = await service.EvaluateCandidatesAsync(
            [candidate.Id], DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(1, result.WouldSubmit);
        Assert.Equal(0, result.Queued);
        var decision = await db.AutomationDecisions.SingleAsync();
        Assert.Equal("WouldSubmit", decision.Outcome);
        Assert.Null(decision.CommandId);
        Assert.Empty(await db.IntegrationOutboxMessages.ToArrayAsync());
    }

    [Fact]
    public async Task Allowlisted_candidate_is_queued_once_across_repeated_evaluation()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var service = Service(db);
        var candidate = await SeedEligibleCandidateAsync(db);
        var current = await service.GetCurrentAsync(CancellationToken.None);
        await ChangePolicyAsync(
            service,
            current.Version,
            AutomationMode.Allowlisted,
            accounts: [candidate.CustomerAccount],
            locations: [candidate.StoreDcGln!]);

        var first = await service.EvaluateCandidatesAsync(
            [candidate.Id], DateTimeOffset.UtcNow, CancellationToken.None);
        var second = await service.EvaluateCandidatesAsync(
            [candidate.Id], DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(1, first.Queued);
        Assert.Equal(1, second.Queued);
        Assert.Single(await db.AutomationDecisions.Where(item => item.Outcome == "Queued").ToArrayAsync());
        var message = Assert.Single(await db.IntegrationOutboxMessages.ToArrayAsync());
        Assert.Equal(IntegrationQueues.ShopriteInvoiceSubmit, message.QueueName);
        Assert.DoesNotContain("manual", message.PayloadJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Daily_cap_limits_a_single_batch_to_the_configured_number_of_commands()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var service = Service(db);
        var firstCandidate = await SeedEligibleCandidateAsync(db, "1");
        var secondCandidate = await SeedEligibleCandidateAsync(db, "2");
        var current = await service.GetCurrentAsync(CancellationToken.None);
        await ChangePolicyAsync(
            service,
            current.Version,
            AutomationMode.Enabled,
            dailyAutomaticSubmissionCap: 1);

        var result = await service.EvaluateCandidatesAsync(
            [firstCandidate.Id, secondCandidate.Id],
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Equal(1, result.Queued);
        Assert.Equal(1, result.Excluded);
        Assert.Single(await db.AutomationDecisions.Where(item => item.Outcome == "Queued").ToArrayAsync());
        Assert.Single(await db.AutomationDecisions.Where(item => item.Outcome == "Capped").ToArrayAsync());
        Assert.Single(await db.IntegrationOutboxMessages.ToArrayAsync());
    }

    [Fact]
    public async Task Emergency_stop_is_versioned_audited_and_blocks_manual_submission()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var service = Service(db);
        var candidate = await SeedEligibleCandidateAsync(db);
        var current = await service.GetCurrentAsync(CancellationToken.None);

        var changed = await service.SetEmergencyStopAsync(
            current.Version,
            true,
            "admin@pvm.co.za",
            "Controlled emergency-stop test.",
            CancellationToken.None);
        var permission = await service.EvaluateSubmissionAsync(
            candidate.Id, "manual", DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.True(changed.Changed);
        Assert.True(changed.Policy.EmergencyStop);
        Assert.False(permission.Allowed);
        Assert.Equal("emergency-stop", permission.Code);
        var audit = await db.AuditEvents.SingleAsync(item => item.Action == "automation-emergency-stop-activated");
        Assert.Equal("admin@pvm.co.za", audit.Actor);
    }

    [Fact]
    public async Task Stale_expected_version_is_rejected_without_writing_another_policy()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var service = Service(db);
        var current = await service.GetCurrentAsync(CancellationToken.None);
        var first = await ChangePolicyAsync(service, current.Version, AutomationMode.Shadow);

        var stale = await ChangePolicyAsync(service, current.Version, AutomationMode.Disabled);

        Assert.True(first.Changed);
        Assert.True(stale.Conflict);
        Assert.Equal(2, await db.AutomationPolicyVersions.CountAsync());
    }

    private AutomationPolicyService Service(PvmDbContext db)
        => new(db, new IntegrationCommandQueue(db));

    private static Task<AutomationPolicyChangeResult> ChangePolicyAsync(
        AutomationPolicyService service,
        int expectedVersion,
        AutomationMode mode,
        string[]? accounts = null,
        string[]? locations = null,
        int dailyAutomaticSubmissionCap = 10)
        => service.ChangeAsync(
            new AutomationPolicyChange(
                expectedVersion,
                mode,
                accounts ?? [],
                locations ?? [],
                ["220"],
                StabilizationDelayMinutes: 0,
                PurchaseOrderFreshnessMinutes: 60,
                AcumaticaFreshnessMinutes: 60,
                DailyAutomaticSubmissionCap: dailyAutomaticSubmissionCap,
                AutomaticWindowStart: new TimeOnly(0, 0),
                AutomaticWindowEnd: new TimeOnly(0, 0),
                TimeZoneId: "Africa/Johannesburg",
                Actor: "admin@pvm.co.za",
                Reason: "Automated policy verification."),
            CancellationToken.None);

    private static async Task<InvoiceCandidateEntity> SeedEligibleCandidateAsync(PvmDbContext db, string suffix = "1")
    {
        var now = DateTimeOffset.UtcNow;
        var purchaseOrder = new ShopritePurchaseOrderEntity
        {
            Id = Guid.NewGuid(),
            PurchaseOrderNumber = $"121538291{suffix}",
            OrderTypeCode = "220",
            SupplierGln = "6001197000006",
            DeliveryGln = "6001001305600",
            DeliveryLocationCode = "30562",
            DeliveryLocationName = "CHECKERS LORRAINE",
            DeliveryLocationSource = "buyer",
            FirstSeenAt = now,
            LastSeenAt = now
        };
        var invoice = new CanonicalInvoice(
            $"acu-invoice-{suffix}",
            $"INV15888{suffix}",
            "1810",
            "30562",
            purchaseOrder.PurchaseOrderNumber,
            purchaseOrder.SupplierGln,
            "4010137059",
            purchaseOrder.DeliveryGln,
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
            MatchedShopritePurchaseOrderId = purchaseOrder.Id,
            SupplierGln = invoice.SupplierGln,
            StoreDcGln = invoice.StoreDcGln,
            IdempotencyKey = $"eligible-policy-test-{suffix}",
            Status = "Ready",
            SourceJson = $"{{\"id\":\"acu-invoice-{suffix}\"}}",
            SourceLastModifiedAt = now,
            CanonicalJson = JsonSerializer.Serialize(invoice, SerializerOptions),
            ValidationJson = JsonSerializer.Serialize(new ValidationResult([]), SerializerOptions),
            CreatedAt = now.AddHours(-1),
            UpdatedAt = now.AddHours(-1)
        };
        db.ShopritePurchaseOrders.Add(purchaseOrder);
        db.InvoiceCandidates.Add(candidate);
        db.IntegrationRuns.AddRange(
            SuccessfulRun(IntegrationRunTypes.ShopritePurchaseOrderRefresh, now),
            SuccessfulRun(IntegrationRunTypes.AcumaticaInvoiceReconciliation, now));
        await db.SaveChangesAsync();
        return candidate;
    }

    private static IntegrationRunEntity SuccessfulRun(string runType, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            RunType = runType,
            Trigger = IntegrationRunTriggers.Scheduled,
            InitiatedBy = "system:test",
            EnvironmentName = "QA",
            CorrelationId = Guid.NewGuid().ToString("D"),
            Status = IntegrationRunStatuses.Succeeded,
            CreatedAt = now.AddMinutes(-1),
            UpdatedAt = now,
            StartedAt = now.AddMinutes(-1),
            CompletedAt = now
        };

    private PvmDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PvmDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new PvmDbContext(options);
    }
}
