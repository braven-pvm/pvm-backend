using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pvm.Application.Acumatica;
using Pvm.Domain.Invoices;
using Pvm.Infrastructure.Acumatica;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Pvm.Infrastructure.Shoprite;
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
            db,
            new ShopriteInvoiceCandidateMatcher(db));

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
    public async Task RefreshAsync_AppliesVerifiedMappingsWhenLivePurchaseOrderHasNoSupplierItemOrUom()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var purchaseOrder = NewPurchaseOrder();
        var purchaseOrderLine = Assert.Single(purchaseOrder.Lines);
        purchaseOrderLine.SupplierItemId = null;
        purchaseOrderLine.BuyerItemId = "10369734";
        purchaseOrderLine.MeasurementUnitCode = null;
        db.ShopritePurchaseOrders.Add(purchaseOrder);
        db.ShopriteItemMappings.Add(new ShopriteItemMappingEntity
        {
            Id = Guid.NewGuid(),
            AcumaticaInventoryId = "ENER10",
            ShopriteBuyerItemId = "10369734",
            Gtin = purchaseOrderLine.Gtin!,
            IsVerified = true,
            UpdatedBy = "admin@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.ShopriteUomMappings.Add(new ShopriteUomMappingEntity
        {
            Id = Guid.NewGuid(),
            AcumaticaInventoryId = "ENER10",
            AcumaticaUom = "BOX",
            ShopriteUom = ShopriteMeasurementUnit.CS,
            IsVerified = true,
            UpdatedBy = "admin@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var source = NewInvoice() with
        {
            Lines =
            [
                NewInvoice().Lines[0] with
                {
                    InventoryId = "ENER10",
                    Uom = "BOX"
                }
            ]
        };
        var service = new AcumaticaInvoiceCandidateRefreshService(
            new StubInvoiceClient([source]),
            db,
            new ShopriteInvoiceCandidateMatcher(db));

        await service.RefreshAsync(CancellationToken.None);

        var candidate = await db.InvoiceCandidates.SingleAsync();
        var canonical = JsonSerializer.Deserialize<CanonicalInvoice>(
            candidate.CanonicalJson!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var line = Assert.Single(Assert.IsType<CanonicalInvoice>(canonical).Lines);
        Assert.Equal("Ready", candidate.Status);
        Assert.Equal("06001197181125", line.Gtin);
        Assert.Equal(ShopriteMeasurementUnit.CS, line.ShopriteUom);
        Assert.True(line.IsShopriteUomVerified);
    }

    [Fact]
    public async Task SaveLineMapping_PersistsAuditsAndRevalidatesCandidate()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var purchaseOrder = NewPurchaseOrder();
        var purchaseOrderLine = Assert.Single(purchaseOrder.Lines);
        purchaseOrderLine.SupplierItemId = null;
        purchaseOrderLine.BuyerItemId = "10369734";
        purchaseOrderLine.MeasurementUnitCode = null;
        db.ShopritePurchaseOrders.Add(purchaseOrder);
        await db.SaveChangesAsync();
        var source = NewInvoice() with
        {
            Lines =
            [
                NewInvoice().Lines[0] with
                {
                    InventoryId = "ENER10",
                    Uom = "BOX"
                }
            ]
        };
        var matcher = new ShopriteInvoiceCandidateMatcher(db);
        var refreshService = new AcumaticaInvoiceCandidateRefreshService(
            new StubInvoiceClient([source]),
            db,
            matcher);
        await refreshService.RefreshAsync(CancellationToken.None);
        var candidate = await db.InvoiceCandidates.SingleAsync();
        Assert.Equal("NeedsReview", candidate.Status);
        var mappingService = new ShopriteInvoiceLineMappingService(db, matcher);

        var result = await mappingService.SaveAsync(
            candidateId: candidate.Id,
            lineNumber: 1,
            purchaseOrderLineId: purchaseOrderLine.Id,
            shopriteUom: ShopriteMeasurementUnit.CS,
            actor: "admin@example.com",
            cancellationToken: CancellationToken.None);

        Assert.Equal(ShopriteLineMappingSaveStatus.Saved, result.Status);
        var savedCandidate = Assert.IsType<InvoiceCandidateEntity>(result.Candidate);
        Assert.Equal("Ready", savedCandidate.Status);
        var savedCanonical = JsonSerializer.Deserialize<CanonicalInvoice>(
            savedCandidate.CanonicalJson!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var savedLine = Assert.Single(Assert.IsType<CanonicalInvoice>(savedCanonical).Lines);
        Assert.Equal(purchaseOrderLine.Gtin, savedLine.Gtin);
        Assert.Equal(ShopriteMeasurementUnit.CS, savedLine.ShopriteUom);
        Assert.True(savedLine.IsShopriteUomVerified);
        var itemMapping = await db.ShopriteItemMappings.SingleAsync();
        Assert.Equal("ENER10", itemMapping.AcumaticaInventoryId);
        Assert.Equal("10369734", itemMapping.ShopriteBuyerItemId);
        Assert.Equal("06001197181125", itemMapping.Gtin);
        Assert.True(itemMapping.IsVerified);
        var uomMapping = await db.ShopriteUomMappings.SingleAsync();
        Assert.Equal("BOX", uomMapping.AcumaticaUom);
        Assert.Equal(ShopriteMeasurementUnit.CS, uomMapping.ShopriteUom);
        Assert.True(uomMapping.IsVerified);
        var audits = await db.AuditEvents.OrderBy(audit => audit.EntityType).ToArrayAsync();
        Assert.Equal(2, audits.Length);
        Assert.All(audits, audit => Assert.Equal("admin@example.com", audit.Actor));
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
            db,
            new ShopriteInvoiceCandidateMatcher(db));

        await service.RefreshAsync(CancellationToken.None);
        var second = await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(0, second.Created);
        Assert.Equal(0, second.Updated);
        Assert.Equal(1, second.Unchanged);
        Assert.Equal(1, await db.InvoiceCandidates.CountAsync());
    }

    [Fact]
    public async Task RefreshAsync_LaterSourceVersionUpdatesExistingCandidateWithoutDuplicate()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        db.ShopritePurchaseOrders.Add(NewPurchaseOrder());
        await db.SaveChangesAsync();
        var original = NewInvoice();
        var matcher = new ShopriteInvoiceCandidateMatcher(db);
        await new AcumaticaInvoiceCandidateRefreshService(
            new StubInvoiceClient([original]),
            db,
            matcher).RefreshAsync(CancellationToken.None);
        var changedAt = original.LastModifiedAt!.Value.AddMinutes(1);

        var result = await new AcumaticaInvoiceCandidateRefreshService(
            new StubInvoiceClient([original with { LastModifiedAt = changedAt }]),
            db,
            matcher).RefreshAsync(CancellationToken.None);

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Updated);
        Assert.Equal(0, result.Unchanged);
        var candidate = await db.InvoiceCandidates.SingleAsync();
        Assert.Equal(changedAt, candidate.SourceLastModifiedAt);
    }

    [Fact]
    public async Task RefreshInvoiceAsync_FetchesOnlyTheAuthoritativeChangedInvoice()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        db.ShopritePurchaseOrders.Add(NewPurchaseOrder());
        await db.SaveChangesAsync();
        var invoice = NewInvoice();
        var service = new AcumaticaInvoiceCandidateRefreshService(
            new StubInvoiceClient([invoice]),
            db,
            new ShopriteInvoiceCandidateMatcher(db));

        var result = await service.RefreshInvoiceAsync(invoice.Id, default);

        Assert.Equal(1, result.Received);
        Assert.Equal(1, result.Created);
        Assert.Equal(invoice.Id, (await db.InvoiceCandidates.SingleAsync()).AcumaticaInvoiceId);
    }

    [Fact]
    public async Task RefreshInvoiceAsync_NoLongerFinalized_BlocksExistingCandidate()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        db.ShopritePurchaseOrders.Add(NewPurchaseOrder());
        await db.SaveChangesAsync();
        var invoice = NewInvoice();
        var matcher = new ShopriteInvoiceCandidateMatcher(db);
        await new AcumaticaInvoiceCandidateRefreshService(
            new StubInvoiceClient([invoice]), db, matcher).RefreshAsync(default);

        var result = await new AcumaticaInvoiceCandidateRefreshService(
            new StubInvoiceClient([]), db, matcher).RefreshInvoiceAsync(invoice.Id, default);

        Assert.Equal(1, result.Updated);
        var candidate = await db.InvoiceCandidates.SingleAsync();
        Assert.Equal("NeedsReview", candidate.Status);
        Assert.Contains("acumatica-invoice-not-finalized", candidate.ValidationJson);
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
            ],
            LastModifiedAt: new DateTimeOffset(2026, 7, 24, 14, 48, 44, TimeSpan.FromHours(2)));

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
            AcumaticaInvoiceQuery? query,
            CancellationToken cancellationToken)
            => Task.FromResult(invoices);

        public Task<AcumaticaInvoiceDto?> FetchFinalizedInvoiceAsync(
            string invoiceId,
            CancellationToken cancellationToken)
            => Task.FromResult(invoices.SingleOrDefault(invoice => invoice.Id == invoiceId));
    }
}
