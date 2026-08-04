using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pvm.Application.Messaging;
using Pvm.Application.Shoprite;
using Pvm.Infrastructure.Messaging;
using Pvm.Infrastructure.Operations;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Operations;

public sealed class ShopritePurchaseOrderRefreshRunTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task ScheduledRun_WithSameScheduleKey_QueuesOnlyOnce()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var queue = new ShopritePurchaseOrderRefreshRunQueue(
            db,
            new IntegrationCommandQueue(db),
            Configuration());

        var first = await queue.EnqueueAsync(
            IntegrationRunTriggers.Scheduled,
            "system:scheduler",
            "shoprite-po-refresh:202608041200",
            CancellationToken.None);
        var second = await queue.EnqueueAsync(
            IntegrationRunTriggers.Scheduled,
            "system:scheduler",
            "shoprite-po-refresh:202608041200",
            CancellationToken.None);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.RunId, second.RunId);
        Assert.Equal(first.MessageId, second.MessageId);
        Assert.Equal(1, await db.IntegrationRuns.CountAsync());
        Assert.Equal(1, await db.IntegrationOutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Freshness_ReportsUnknownStaleAndHealthyFromSuccessfulRuns()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var now = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
        var service = new ShopritePurchaseOrderFreshnessService(
            db,
            Options.Create(new ShopritePurchaseOrderRefreshOptions
            {
                ScheduleIntervalMinutes = 5,
                StaleAfterMinutes = 15
            }));

        Assert.Equal("Unknown", (await service.GetAsync(now, CancellationToken.None)).Status);

        var run = NewSucceededRun(now.AddMinutes(-20));
        db.IntegrationRuns.Add(run);
        await db.SaveChangesAsync();
        var stale = await service.GetAsync(now, CancellationToken.None);
        Assert.Equal("Stale", stale.Status);
        Assert.False(stale.AllowsAutomaticProcessing);

        run.CompletedAt = now.AddMinutes(-2);
        run.UpdatedAt = now.AddMinutes(-2);
        await db.SaveChangesAsync();
        var healthy = await service.GetAsync(now, CancellationToken.None);
        Assert.Equal("Healthy", healthy.Status);
        Assert.True(healthy.AllowsAutomaticProcessing);
    }

    [Fact]
    public async Task FailedFetch_RecordsFailedRunAndPreservesExistingPurchaseOrders()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        db.ShopritePurchaseOrders.Add(new ShopritePurchaseOrderEntity
        {
            Id = Guid.NewGuid(),
            PurchaseOrderNumber = "PO-EXISTING",
            DeliveryLocationSource = "buyer",
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var runId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var command = new RefreshShopritePurchaseOrdersMessage(
            "system:scheduler",
            runId,
            IntegrationRunTriggers.Scheduled);
        var handler = new ShopritePurchaseOrderRefreshMessageHandler(
            new FailingPurchaseOrderClient(),
            null!,
            null!,
            new IntegrationRunService(db, Configuration()),
            NullLogger<ShopritePurchaseOrderRefreshMessageHandler>.Instance);
        var envelope = new IntegrationMessageEnvelope(
            messageId,
            IntegrationMessageTypes.ShopritePurchaseOrderRefreshV1,
            runId.ToString("D"),
            null,
            DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(command));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            handler.HandleAsync(envelope, command, CancellationToken.None));

        Assert.Equal(1, await db.ShopritePurchaseOrders.CountAsync());
        var run = await db.IntegrationRuns.SingleAsync(item => item.Id == runId);
        Assert.Equal(IntegrationRunStatuses.Failed, run.Status);
        Assert.Equal(1, run.FailedCount);
        Assert.Equal("shoprite-po-refresh-failed", run.ErrorCode);
    }

    private PvmDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PvmDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new PvmDbContext(options);
    }

    private static IConfiguration Configuration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Pvm:EnvironmentName"] = "Test"
            })
            .Build();

    private static IntegrationRunEntity NewSucceededRun(DateTimeOffset completedAt)
        => new()
        {
            Id = Guid.NewGuid(),
            RunType = IntegrationRunTypes.ShopritePurchaseOrderRefresh,
            Trigger = IntegrationRunTriggers.Scheduled,
            InitiatedBy = "system:scheduler",
            EnvironmentName = "Test",
            CorrelationId = Guid.NewGuid().ToString("D"),
            Status = IntegrationRunStatuses.Succeeded,
            CreatedAt = completedAt.AddMinutes(-1),
            UpdatedAt = completedAt,
            StartedAt = completedAt.AddSeconds(-10),
            CompletedAt = completedAt
        };

    private sealed class FailingPurchaseOrderClient : IShopritePurchaseOrderClient
    {
        public Task<ShopritePurchaseOrderBatch> FetchAsync(CancellationToken cancellationToken)
            => throw new HttpRequestException("Shoprite unavailable.");
    }
}
