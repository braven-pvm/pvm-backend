using Microsoft.EntityFrameworkCore;
using Pvm.Application.Shoprite;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Pvm.Infrastructure.Shoprite;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Shoprite;

public sealed class ShopritePurchaseOrderRefreshServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task RefreshAsync_ReplacesExistingLinesWhenTrackedLineRowsAreAlreadyGone()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var order = NewPurchaseOrder();
        order.Lines.Add(new ShopritePurchaseOrderLineEntity
        {
            Id = Guid.NewGuid(),
            LineNumber = 1,
            Gtin = "stale-gtin"
        });
        db.ShopritePurchaseOrders.Add(order);
        await db.SaveChangesAsync();

        await db.ShopritePurchaseOrders
            .Include(storedOrder => storedOrder.Lines)
            .SingleAsync(storedOrder => storedOrder.Id == order.Id);
        await using (var concurrentDb = CreateDbContext())
        {
            await concurrentDb.Database.ExecuteSqlRawAsync(
                """delete from shoprite_purchase_order_lines where "ShopritePurchaseOrderId" = {0}""",
                order.Id);
        }

        var service = new ShopritePurchaseOrderRefreshService(db);
        var refreshedAt = new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

        var result = await service.RefreshAsync(NewBatch(order.PurchaseOrderNumber), refreshedAt, CancellationToken.None);

        Assert.Equal(1, result.Received);
        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Updated);
        Assert.Equal(0, result.Unchanged);
        Assert.Equal(0, result.Skipped);

        await using var assertionDb = CreateDbContext();
        var refreshedOrder = await assertionDb.ShopritePurchaseOrders
            .Include(storedOrder => storedOrder.Lines)
            .SingleAsync(storedOrder => storedOrder.Id == order.Id);
        Assert.Equal(refreshedAt, refreshedOrder.LastSeenAt);
        Assert.Equal(["new-gtin-1", "new-gtin-2"], refreshedOrder.Lines
            .OrderBy(line => line.LineNumber)
            .Select(line => line.Gtin ?? string.Empty)
            .ToArray());
    }

    [Fact]
    public async Task RefreshAsync_IdenticalPayloadKeepsExistingLinesAndReportsUnchanged()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var service = new ShopritePurchaseOrderRefreshService(db);
        var batch = NewBatch("1210297232");

        var first = await service.RefreshAsync(
            batch,
            new DateTimeOffset(2026, 8, 4, 8, 0, 0, TimeSpan.Zero),
            CancellationToken.None);
        var secondSeenAt = new DateTimeOffset(2026, 8, 4, 8, 5, 0, TimeSpan.Zero);
        var second = await service.RefreshAsync(batch, secondSeenAt, CancellationToken.None);

        Assert.Equal(1, first.Created);
        Assert.Equal(0, second.Created);
        Assert.Equal(0, second.Updated);
        Assert.Equal(1, second.Unchanged);
        Assert.Empty(second.ChangedPurchaseOrderNumbers);
        Assert.Equal(1, await db.ShopritePurchaseOrders.CountAsync());
        Assert.Equal(2, await db.ShopritePurchaseOrderLines.CountAsync());
        Assert.Equal(secondSeenAt, (await db.ShopritePurchaseOrders.SingleAsync()).LastSeenAt);
    }

    private PvmDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PvmDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        return new PvmDbContext(options);
    }

    private static ShopritePurchaseOrderEntity NewPurchaseOrder()
        => new()
        {
            Id = Guid.NewGuid(),
            PurchaseOrderNumber = "1210297232",
            DeliveryLocationSource = "buyer",
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow
        };

    private static ShopritePurchaseOrderBatch NewBatch(string purchaseOrderNumber)
        => new(
            RawPayload: """{"orders":[{"purchaseOrderNumber":"1210297232"}]}""",
            Orders:
            [
                new ShopritePurchaseOrder(
                    PurchaseOrderNumber: purchaseOrderNumber,
                    OrderHeaderId: "header-1",
                    OrderTypeCode: "220",
                    OrderTypeLabel: "Normal",
                    SupplierGln: "6001197000006",
                    BuyerGln: "6001001018104",
                    DeliveryGln: "6001001829106",
                    DeliveryLocationCode: "82917",
                    DeliveryLocationName: "CHECKERS FX PLETTENBERG BAY MALL",
                    DeliveryLocationSource: "buyer",
                    CurrencyCode: "ZAR",
                    TotalExcludingTax: 200m,
                    TotalIncludingTax: 230m,
                    TotalTax: 30m,
                    CreatedAt: new DateTimeOffset(2026, 7, 8, 12, 17, 21, TimeSpan.Zero),
                    LastUpdatedAt: new DateTimeOffset(2026, 7, 8, 12, 17, 21, TimeSpan.Zero),
                    RawOrderJson: """{"purchaseOrderNumber":"1210297232"}""",
                    Lines:
                    [
                        new ShopritePurchaseOrderLine(
                            LineNumber: 1,
                            Gtin: "new-gtin-1",
                            BuyerItemId: "buyer-item-1",
                            BuyerItemDescription: "Product one",
                            SupplierItemId: "supplier-item-1",
                            Description: "Product one",
                            RequestedQuantity: 2m,
                            MeasurementUnitCode: "EA",
                            NetAmount: 100m,
                            NetPrice: 50m,
                            MonetaryAmountExcludingTaxes: 100m,
                            MonetaryAmountIncludingTaxes: 115m),
                        new ShopritePurchaseOrderLine(
                            LineNumber: 2,
                            Gtin: "new-gtin-2",
                            BuyerItemId: "buyer-item-2",
                            BuyerItemDescription: "Product two",
                            SupplierItemId: "supplier-item-2",
                            Description: "Product two",
                            RequestedQuantity: 1m,
                            MeasurementUnitCode: "EA",
                            NetAmount: 100m,
                            NetPrice: 100m,
                            MonetaryAmountExcludingTaxes: 100m,
                            MonetaryAmountIncludingTaxes: 115m)
                    ])
            ]);
}
