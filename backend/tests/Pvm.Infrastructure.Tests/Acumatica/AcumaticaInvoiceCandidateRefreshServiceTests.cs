using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pvm.Application.Acumatica;
using Pvm.Domain.Invoices;
using Pvm.Infrastructure.Acumatica;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Acumatica;

public sealed class AcumaticaInvoiceCandidateRefreshServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task RefreshAsync_PersistsCandidateMatchedAndEnrichedFromShopritePurchaseOrder()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var purchaseOrder = NewPurchaseOrder();
        db.ShopritePurchaseOrders.Add(purchaseOrder);
        await db.SaveChangesAsync();
        var service = new AcumaticaInvoiceCandidateRefreshService(
            new StubInvoiceClient([NewInvoice()]),
            db);

        var result = await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(1, result.Received);
        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Updated);

        var candidate = await db.InvoiceCandidates.SingleAsync();
        Assert.Equal("INV000123", candidate.InvoiceNumber);
        Assert.Equal(purchaseOrder.Id, candidate.MatchedShopritePurchaseOrderId);
        Assert.Equal("6001197000006", candidate.SupplierGln);
        Assert.Equal("6001001829106", candidate.StoreDcGln);
        Assert.Equal("Ready", candidate.Status);

        var canonical = JsonSerializer.Deserialize<CanonicalInvoice>(
            candidate.CanonicalJson!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(canonical);
        Assert.Equal("06001197181125", Assert.Single(canonical.Lines).Gtin);
    }

    [Fact]
    public async Task RefreshAsync_RepeatedInvoiceUpdatesExistingCandidateWithoutDuplicate()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        db.ShopritePurchaseOrders.Add(NewPurchaseOrder());
        await db.SaveChangesAsync();
        var service = new AcumaticaInvoiceCandidateRefreshService(
            new StubInvoiceClient([NewInvoice()]),
            db);

        await service.RefreshAsync(CancellationToken.None);
        var second = await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(0, second.Created);
        Assert.Equal(1, second.Updated);
        Assert.Equal(1, await db.InvoiceCandidates.CountAsync());
    }

    private PvmDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PvmDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new PvmDbContext(options);
    }

    private static AcumaticaInvoiceDto NewInvoice()
        => new(
            Id: "c340d968-71a3-4ced-8c91-7cf1e653bec4",
            InvoiceNumber: "INV000123",
            Status: "Open",
            CustomerAccount: "SHOPRITE",
            CustomerLocation: "CPT-DC",
            CustomerOrder: "1210297232",
            CurrencyCode: "ZAR",
            CountryCode: "ZA",
            InvoiceDate: new DateTimeOffset(2026, 7, 14, 0, 0, 0, TimeSpan.FromHours(2)),
            TotalExcludingTax: 100m,
            TotalIncludingTax: 115m,
            TotalTax: 15m,
            Lines:
            [
                new AcumaticaInvoiceLineDto(
                    LineNumber: 1,
                    InventoryId: "PVM-ITEM-1",
                    Gtin: null,
                    Description: "PVM test item",
                    Quantity: 2m,
                    Uom: "EA",
                    PackSize: 1m,
                    UnitAmountExcludingTax: 50m,
                    UnitAmountIncludingTax: 57.5m,
                    TaxAmount: 7.5m,
                    TaxCategoryCode: "STANDARD",
                    TaxPercentage: 15m,
                    IsCatchWeight: false)
            ]);

    private static ShopritePurchaseOrderEntity NewPurchaseOrder()
    {
        var order = new ShopritePurchaseOrderEntity
        {
            Id = Guid.NewGuid(),
            PurchaseOrderNumber = "1210297232",
            SupplierGln = "6001197000006",
            DeliveryGln = "6001001829106",
            DeliveryLocationCode = "82917",
            DeliveryLocationName = "CHECKERS FX PLETTENBERG BAY MALL",
            DeliveryLocationSource = "buyer",
            CurrencyCode = "ZAR",
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
            RawOrderJson = """{"purchaseOrderNumber":"1210297232"}"""
        };
        order.Lines.Add(new ShopritePurchaseOrderLineEntity
        {
            Id = Guid.NewGuid(),
            LineNumber = 1,
            Gtin = "06001197181125",
            SupplierItemId = "PVM-ITEM-1",
            Description = "PVM test item",
            RequestedQuantity = 2m,
            MeasurementUnitCode = "EA",
            NetPrice = 50m,
            MonetaryAmountExcludingTaxes = 100m,
            MonetaryAmountIncludingTaxes = 115m
        });
        return order;
    }

    private sealed class StubInvoiceClient(IReadOnlyList<AcumaticaInvoiceDto> invoices)
        : IAcumaticaInvoiceClient
    {
        public Task<IReadOnlyList<AcumaticaInvoiceDto>> FetchFinalizedInvoicesAsync(
            CancellationToken cancellationToken)
            => Task.FromResult(invoices);
    }
}
