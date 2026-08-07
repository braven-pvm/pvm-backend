using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pvm.Application.Acumatica;
using Pvm.Application.Messaging;
using Pvm.Infrastructure.Acumatica;
using Pvm.Infrastructure.Messaging;
using Pvm.Infrastructure.Operations;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Pvm.Infrastructure.Shoprite;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Operations;

public sealed class AcumaticaInvoiceReconciliationRunTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task ScheduledRun_WithSameScheduleKey_QueuesOnlyOnce()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var queue = NewQueue(db);
        var queryTo = new DateTimeOffset(2026, 8, 6, 12, 10, 0, TimeSpan.Zero);

        var first = await queue.EnqueueAsync(
            IntegrationRunTriggers.Scheduled,
            "system:scheduler",
            "acumatica-invoice-reconciliation:202608061210",
            queryTo,
            lookbackDays: null,
            CancellationToken.None);
        var second = await queue.EnqueueAsync(
            IntegrationRunTriggers.Scheduled,
            "system:scheduler",
            "acumatica-invoice-reconciliation:202608061210",
            queryTo,
            lookbackDays: null,
            CancellationToken.None);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.RunId, second.RunId);
        Assert.Equal(first.MessageId, second.MessageId);
        Assert.Equal(1, await db.IntegrationRuns.CountAsync());
        Assert.Equal(1, await db.IntegrationOutboxMessages.CountAsync());
    }

    [Fact]
    public async Task SuccessfulRuns_BootstrapThenUseOverlappingCursor()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var client = new RecordingInvoiceClient();
        var handler = NewHandler(db, client);
        var firstQueryTo = new DateTimeOffset(2026, 8, 6, 12, 10, 0, TimeSpan.Zero);

        var first = await QueueAndHandleAsync(db, handler, firstQueryTo);

        var firstQuery = Assert.Single(client.Queries);
        Assert.Equal(firstQueryTo.AddDays(-7), firstQuery.ModifiedFromInclusive);
        Assert.Equal(firstQueryTo, firstQuery.ModifiedToExclusive);
        var firstRun = await db.IntegrationRuns.SingleAsync(run => run.Id == first.RunId);
        Assert.Equal(IntegrationRunStatuses.Succeeded, firstRun.Status);
        Assert.Null(firstRun.CursorBefore);
        Assert.Equal(firstQueryTo, firstRun.CursorAfter);

        var secondQueryTo = firstQueryTo.AddMinutes(10);
        var second = await QueueAndHandleAsync(db, handler, secondQueryTo);

        var secondQuery = client.Queries[1];
        Assert.Equal(firstQueryTo.AddMinutes(-15), secondQuery.ModifiedFromInclusive);
        Assert.Equal(secondQueryTo, secondQuery.ModifiedToExclusive);
        var secondRun = await db.IntegrationRuns.SingleAsync(run => run.Id == second.RunId);
        Assert.Equal(firstQueryTo, secondRun.CursorBefore);
        Assert.Equal(secondQueryTo, secondRun.CursorAfter);
    }

    [Fact]
    public async Task FailedRun_DoesNotAdvanceCursor()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var client = new RecordingInvoiceClient();
        var handler = NewHandler(db, client);
        var successfulCursor = new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
        await QueueAndHandleAsync(db, handler, successfulCursor);
        client.Failure = new HttpRequestException("Acumatica unavailable.");
        var failedQueryTo = successfulCursor.AddMinutes(10);

        var queued = await NewQueue(db).EnqueueAsync(
            IntegrationRunTriggers.Scheduled,
            "system:scheduler",
            scheduleKey: null,
            failedQueryTo,
            lookbackDays: null,
            CancellationToken.None);
        var command = NewCommand(queued.RunId, failedQueryTo);
        await Assert.ThrowsAsync<HttpRequestException>(() => handler.HandleAsync(
            NewEnvelope(queued.MessageId, command),
            command,
            CancellationToken.None));

        var failedRun = await db.IntegrationRuns.SingleAsync(run => run.Id == queued.RunId);
        Assert.Equal(IntegrationRunStatuses.Failed, failedRun.Status);
        Assert.Equal(successfulCursor, failedRun.CursorBefore);
        Assert.Null(failedRun.CursorAfter);
        var latestCursor = await new IntegrationRunService(db, Configuration())
            .GetLatestSuccessfulAcumaticaCursorAsync(failedQueryTo, CancellationToken.None);
        Assert.Equal(successfulCursor, latestCursor);
    }

    [Fact]
    public async Task DailyLookback_UsesConfiguredWiderWindow()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var client = new RecordingInvoiceClient();
        var handler = NewHandler(db, client);
        var queryTo = new DateTimeOffset(2026, 8, 6, 0, 0, 0, TimeSpan.Zero);
        var queued = await NewQueue(db).EnqueueAsync(
            IntegrationRunTriggers.DailyLookback,
            "system:scheduler",
            scheduleKey: null,
            queryTo,
            lookbackDays: 7,
            CancellationToken.None);
        var command = NewCommand(queued.RunId, queryTo, IntegrationRunTriggers.DailyLookback, 7);

        await handler.HandleAsync(NewEnvelope(queued.MessageId, command), command, CancellationToken.None);

        var query = Assert.Single(client.Queries);
        Assert.Equal(queryTo.AddDays(-7), query.ModifiedFromInclusive);
        Assert.Equal(queryTo, query.ModifiedToExclusive);
    }

    [Fact]
    public async Task Freshness_ReportsUnknownHealthyAndStaleFromSuccessfulRuns()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var now = new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
        var service = new AcumaticaInvoiceReconciliationFreshnessService(
            db,
            Options.Create(new AcumaticaReconciliationOptions { StaleAfterMinutes = 30 }));

        Assert.Equal("Unknown", (await service.GetAsync(now, CancellationToken.None)).Status);

        var run = new IntegrationRunEntity
        {
            Id = Guid.NewGuid(),
            RunType = IntegrationRunTypes.AcumaticaInvoiceReconciliation,
            Trigger = IntegrationRunTriggers.Scheduled,
            InitiatedBy = "system:scheduler",
            EnvironmentName = "Test",
            CorrelationId = Guid.NewGuid().ToString("D"),
            Status = IntegrationRunStatuses.Succeeded,
            CursorAfter = now.AddMinutes(-10),
            CreatedAt = now.AddMinutes(-11),
            UpdatedAt = now.AddMinutes(-10),
            CompletedAt = now.AddMinutes(-10)
        };
        db.IntegrationRuns.Add(run);
        await db.SaveChangesAsync();

        var healthy = await service.GetAsync(now, CancellationToken.None);
        Assert.Equal("Healthy", healthy.Status);
        Assert.Equal(run.CursorAfter, healthy.CursorAfter);

        run.CompletedAt = now.AddMinutes(-31);
        run.UpdatedAt = now.AddMinutes(-31);
        await db.SaveChangesAsync();
        Assert.Equal("Stale", (await service.GetAsync(now, CancellationToken.None)).Status);
    }

    private async Task<QueuedIntegrationRun> QueueAndHandleAsync(
        PvmDbContext db,
        AcumaticaInvoiceReconciliationMessageHandler handler,
        DateTimeOffset queryTo)
    {
        var queued = await NewQueue(db).EnqueueAsync(
            IntegrationRunTriggers.Scheduled,
            "system:scheduler",
            scheduleKey: null,
            queryTo,
            lookbackDays: null,
            CancellationToken.None);
        var command = NewCommand(queued.RunId, queryTo);
        await handler.HandleAsync(NewEnvelope(queued.MessageId, command), command, CancellationToken.None);
        return queued;
    }

    private AcumaticaInvoiceReconciliationMessageHandler NewHandler(
        PvmDbContext db,
        RecordingInvoiceClient client)
        => new(
            new AcumaticaInvoiceCandidateRefreshService(
                client,
                db,
                new ShopriteInvoiceCandidateMatcher(db)),
            new IntegrationRunService(db, Configuration()),
            Options.Create(new AcumaticaOptions
            {
                InvoiceDateFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)
            }),
            Options.Create(new AcumaticaReconciliationOptions
            {
                OverlapMinutes = 15,
                DailyLookbackDays = 7
            }),
            NullLogger<AcumaticaInvoiceReconciliationMessageHandler>.Instance);

    private AcumaticaInvoiceReconciliationRunQueue NewQueue(PvmDbContext db)
        => new(db, new IntegrationCommandQueue(db), Configuration());

    private PvmDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PvmDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new PvmDbContext(options);
    }

    private static ReconcileAcumaticaInvoicesMessage NewCommand(
        Guid runId,
        DateTimeOffset queryTo,
        string trigger = IntegrationRunTriggers.Scheduled,
        int? lookbackDays = null)
        => new("system:scheduler", runId, trigger, queryTo, lookbackDays);

    private static IntegrationMessageEnvelope NewEnvelope(
        Guid messageId,
        ReconcileAcumaticaInvoicesMessage command)
        => new(
            messageId,
            IntegrationMessageTypes.AcumaticaInvoiceReconciliationV1,
            command.RunId.ToString("D"),
            null,
            DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(command));

    private static IConfiguration Configuration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Pvm:EnvironmentName"] = "Test"
            })
            .Build();

    private sealed class RecordingInvoiceClient : IAcumaticaInvoiceClient
    {
        public List<AcumaticaInvoiceQuery> Queries { get; } = [];

        public Exception? Failure { get; set; }

        public Task<IReadOnlyList<AcumaticaInvoiceDto>> FetchFinalizedInvoicesAsync(
            AcumaticaInvoiceQuery? query,
            CancellationToken cancellationToken)
        {
            if (query is null)
            {
                throw new InvalidOperationException("A bounded query is required.");
            }

            Queries.Add(query);
            return Failure is null
                ? Task.FromResult<IReadOnlyList<AcumaticaInvoiceDto>>([])
                : Task.FromException<IReadOnlyList<AcumaticaInvoiceDto>>(Failure);
        }

        public Task<AcumaticaInvoiceDto?> FetchFinalizedInvoiceAsync(
            string invoiceId,
            CancellationToken cancellationToken)
            => Task.FromResult<AcumaticaInvoiceDto?>(null);
    }
}
