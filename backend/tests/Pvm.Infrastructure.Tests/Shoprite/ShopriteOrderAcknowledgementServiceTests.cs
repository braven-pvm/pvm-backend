using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pvm.Application.Shoprite;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Pvm.Infrastructure.Shoprite;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Shoprite;

public sealed class ShopriteOrderAcknowledgementServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Stored_orders_are_acknowledged_once_and_recorded()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        await SeedOrderAsync(db, "1212021109");
        await SeedOrderAsync(db, "1215382915");
        var client = new RecordingPurchaseOrderClient();
        var service = Service(db, client, acknowledge: true);
        var now = DateTimeOffset.UtcNow;

        var first = await service.AcknowledgeFetchedOrdersAsync(["1212021109", "1215382915"], now, CancellationToken.None);
        var second = await service.AcknowledgeFetchedOrdersAsync(["1212021109", "1215382915"], now, CancellationToken.None);

        Assert.True(first.Enabled);
        Assert.Equal(2, first.Acknowledged);
        Assert.Equal(0, first.Pending);
        Assert.Equal(0, second.Acknowledged);
        Assert.Single(client.AcknowledgedBatches);
        Assert.Equal(["1212021109", "1215382915"], client.AcknowledgedBatches[0].Order());
        Assert.All(
            await db.ShopritePurchaseOrders.ToListAsync(),
            order =>
            {
                Assert.NotNull(order.AcknowledgedAt);
                Assert.Equal(1, order.AcknowledgementAttempts);
                Assert.Null(order.LastAcknowledgementError);
            });
    }

    [Fact]
    public async Task A_failed_acknowledgement_keeps_the_orders_pending_and_records_the_error()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        await SeedOrderAsync(db, "1212021109");
        var client = new RecordingPurchaseOrderClient { FailWith = "Shoprite returned HTTP 502." };
        var service = Service(db, client, acknowledge: true);

        var result = await service.AcknowledgeFetchedOrdersAsync(["1212021109"], DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(0, result.Acknowledged);
        Assert.Equal(1, result.Pending);
        Assert.Contains("502", result.Error);
        var order = await db.ShopritePurchaseOrders.SingleAsync();
        Assert.Null(order.AcknowledgedAt);
        Assert.Equal(1, order.AcknowledgementAttempts);
        Assert.Contains("502", order.LastAcknowledgementError);
    }

    [Fact]
    public async Task A_failed_acknowledgement_is_retried_on_the_next_run()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        await SeedOrderAsync(db, "1212021109");
        var client = new RecordingPurchaseOrderClient { FailWith = "Shoprite returned HTTP 502." };
        var service = Service(db, client, acknowledge: true);
        await service.AcknowledgeFetchedOrdersAsync(["1212021109"], DateTimeOffset.UtcNow, CancellationToken.None);

        client.FailWith = null;
        var result = await service.AcknowledgeFetchedOrdersAsync(["1212021109"], DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(1, result.Acknowledged);
        var order = await db.ShopritePurchaseOrders.SingleAsync();
        Assert.NotNull(order.AcknowledgedAt);
        Assert.Equal(2, order.AcknowledgementAttempts);
        Assert.Null(order.LastAcknowledgementError);
    }

    [Fact]
    public async Task Acknowledgement_is_skipped_while_the_setting_is_off()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        await SeedOrderAsync(db, "1212021109");
        var client = new RecordingPurchaseOrderClient();
        var service = Service(db, client, acknowledge: false);

        var result = await service.AcknowledgeFetchedOrdersAsync(["1212021109"], DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(result.Enabled);
        Assert.Equal(1, result.Pending);
        Assert.Empty(client.AcknowledgedBatches);
        Assert.Null((await db.ShopritePurchaseOrders.SingleAsync()).AcknowledgedAt);
    }

    [Fact]
    public async Task A_reset_clears_the_local_acknowledgement()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        await SeedOrderAsync(db, "1212021109");
        var client = new RecordingPurchaseOrderClient();
        var service = Service(db, client, acknowledge: true);
        await service.AcknowledgeFetchedOrdersAsync(["1212021109"], DateTimeOffset.UtcNow, CancellationToken.None);

        var reset = await service.ResetAsync(["1212021109"], CancellationToken.None);

        Assert.Equal(1, reset);
        Assert.Equal(["1212021109"], client.ResetBatches.Single());
        Assert.Null((await db.ShopritePurchaseOrders.SingleAsync()).AcknowledgedAt);
    }

    [Fact]
    public async Task An_order_that_shoprite_no_longer_offers_is_never_acknowledged()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        await SeedOrderAsync(db, "1212021109");
        await SeedOrderAsync(db, "1299999999");
        var client = new RecordingPurchaseOrderClient();
        var service = Service(db, client, acknowledge: true);

        var result = await service.AcknowledgeFetchedOrdersAsync(
            ["1212021109"],
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Equal(1, result.Acknowledged);
        Assert.Equal(["1212021109"], client.AcknowledgedBatches.Single());
        var stale = await db.ShopritePurchaseOrders.SingleAsync(order => order.PurchaseOrderNumber == "1299999999");
        Assert.Null(stale.AcknowledgedAt);
        Assert.Equal(0, stale.AcknowledgementAttempts);
    }

    [Fact]
    public async Task An_empty_fetch_acknowledges_nothing()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        await SeedOrderAsync(db, "1212021109");
        var client = new RecordingPurchaseOrderClient();
        var service = Service(db, client, acknowledge: true);

        var result = await service.AcknowledgeFetchedOrdersAsync([], DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.True(result.Enabled);
        Assert.Equal(0, result.Acknowledged);
        Assert.Empty(client.AcknowledgedBatches);
    }

    private static ShopriteOrderAcknowledgementService Service(
        PvmDbContext db,
        IShopritePurchaseOrderClient client,
        bool acknowledge)
        => new(
            db,
            client,
            Options.Create(new ShopriteOptions
            {
                BaseUrl = "https://shoprite.example/api",
                Username = "api-user",
                Password = "secret",
                ContractId = "contract-123",
                AcknowledgeOrders = acknowledge
            }));

    private static async Task SeedOrderAsync(PvmDbContext db, string purchaseOrderNumber)
    {
        var now = DateTimeOffset.UtcNow;
        db.ShopritePurchaseOrders.Add(new ShopritePurchaseOrderEntity
        {
            Id = Guid.NewGuid(),
            PurchaseOrderNumber = purchaseOrderNumber,
            OrderTypeCode = "220",
            DeliveryLocationSource = "buyer",
            FirstSeenAt = now,
            LastSeenAt = now
        });
        await db.SaveChangesAsync();
    }

    private PvmDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PvmDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new PvmDbContext(options);
    }

    private sealed class RecordingPurchaseOrderClient : IShopritePurchaseOrderClient
    {
        public List<string[]> AcknowledgedBatches { get; } = [];

        public List<string[]> ResetBatches { get; } = [];

        public string? FailWith { get; set; }

        public Task<ShopritePurchaseOrderBatch> FetchAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task AcknowledgeAsync(
            IReadOnlyCollection<string> purchaseOrderNumbers,
            CancellationToken cancellationToken)
        {
            if (FailWith is not null)
            {
                throw new InvalidOperationException(FailWith);
            }

            AcknowledgedBatches.Add([.. purchaseOrderNumbers]);
            return Task.CompletedTask;
        }

        public Task ResetAsync(
            IReadOnlyCollection<string> purchaseOrderNumbers,
            CancellationToken cancellationToken)
        {
            ResetBatches.Add([.. purchaseOrderNumbers]);
            return Task.CompletedTask;
        }
    }
}

internal static class OrderingExtensions
{
    public static string[] Order(this string[] values)
    {
        var copy = (string[])values.Clone();
        Array.Sort(copy, StringComparer.Ordinal);
        return copy;
    }
}
