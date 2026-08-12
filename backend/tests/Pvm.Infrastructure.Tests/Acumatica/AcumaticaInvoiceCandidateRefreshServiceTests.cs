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
    public async Task RefreshAsync_BootstrapsGlobalMappingForUniqueExactGtinAndUom()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var purchaseOrder = NewPurchaseOrder();
        var purchaseOrderLine = Assert.Single(purchaseOrder.Lines);
        purchaseOrderLine.BuyerItemId = "10369734";
        db.ShopritePurchaseOrders.Add(purchaseOrder);
        await db.SaveChangesAsync();
        var source = NewInvoice() with
        {
            Lines =
            [
                NewInvoice().Lines[0] with
                {
                    Gtin = purchaseOrderLine.Gtin
                }
            ]
        };
        var service = new AcumaticaInvoiceCandidateRefreshService(
            new StubInvoiceClient([source]),
            db,
            new ShopriteInvoiceCandidateMatcher(db));

        await service.RefreshAsync(CancellationToken.None);

        var itemMapping = await db.ShopriteItemMappings.SingleAsync();
        Assert.Equal("PVM-ITEM-1", itemMapping.AcumaticaInventoryId);
        Assert.Equal("10369734", itemMapping.ShopriteBuyerItemId);
        Assert.Equal(purchaseOrderLine.Gtin, itemMapping.Gtin);
        Assert.True(itemMapping.IsVerified);
        Assert.Equal("system:inventory-mapping-bootstrap", itemMapping.UpdatedBy);
        var uomMapping = await db.ShopriteUomMappings.SingleAsync();
        Assert.Equal("EA", uomMapping.AcumaticaUom);
        Assert.Equal(ShopriteMeasurementUnit.EA, uomMapping.ShopriteUom);
        Assert.True(uomMapping.IsVerified);
        Assert.Equal("system:inventory-mapping-bootstrap", uomMapping.UpdatedBy);
        var audits = await db.AuditEvents.ToArrayAsync();
        Assert.Equal(2, audits.Length);
        Assert.All(audits, audit =>
        {
            Assert.Equal("system:inventory-mapping-bootstrap", audit.Actor);
            Assert.Contains(purchaseOrder.PurchaseOrderNumber, audit.DetailsJson);
            Assert.Contains(purchaseOrderLine.Id.ToString(), audit.DetailsJson);
        });
        var candidate = await db.InvoiceCandidates.SingleAsync();
        var canonical = JsonSerializer.Deserialize<CanonicalInvoice>(
            candidate.CanonicalJson!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var canonicalLine = Assert.Single(Assert.IsType<CanonicalInvoice>(canonical).Lines);
        Assert.Equal(ShopriteMeasurementUnit.EA, canonicalLine.ShopriteUom);
        Assert.True(canonicalLine.IsShopriteUomVerified);
        Assert.Equal("Ready", candidate.Status);
    }

    [Fact]
    public async Task RefreshAsync_DoesNotBootstrapWhenShopriteUomDiffers()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var purchaseOrder = NewPurchaseOrder();
        var purchaseOrderLine = Assert.Single(purchaseOrder.Lines);
        purchaseOrderLine.BuyerItemId = "10369734";
        purchaseOrderLine.MeasurementUnitCode = "CS";
        db.ShopritePurchaseOrders.Add(purchaseOrder);
        await db.SaveChangesAsync();
        var source = NewInvoice() with
        {
            Lines =
            [
                NewInvoice().Lines[0] with
                {
                    Gtin = purchaseOrderLine.Gtin
                }
            ]
        };
        var service = new AcumaticaInvoiceCandidateRefreshService(
            new StubInvoiceClient([source]),
            db,
            new ShopriteInvoiceCandidateMatcher(db));

        await service.RefreshAsync(CancellationToken.None);

        Assert.Empty(await db.ShopriteItemMappings.ToArrayAsync());
        Assert.Empty(await db.ShopriteUomMappings.ToArrayAsync());
        Assert.Empty(await db.AuditEvents.ToArrayAsync());
    }

    [Fact]
    public async Task RefreshAsync_ConflictingGlobalMappingDoesNotCreatePartialBootstrap()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var purchaseOrder = NewPurchaseOrder();
        var purchaseOrderLine = Assert.Single(purchaseOrder.Lines);
        purchaseOrderLine.BuyerItemId = "10369734";
        db.ShopritePurchaseOrders.Add(purchaseOrder);
        db.ShopriteUomMappings.Add(new ShopriteUomMappingEntity
        {
            Id = Guid.NewGuid(),
            AcumaticaInventoryId = "PVM-ITEM-1",
            AcumaticaUom = "EA",
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
                    Gtin = purchaseOrderLine.Gtin
                }
            ]
        };
        var service = new AcumaticaInvoiceCandidateRefreshService(
            new StubInvoiceClient([source]),
            db,
            new ShopriteInvoiceCandidateMatcher(db));

        await service.RefreshAsync(CancellationToken.None);

        Assert.Empty(await db.ShopriteItemMappings.ToArrayAsync());
        Assert.Single(await db.ShopriteUomMappings.ToArrayAsync());
        Assert.Empty(await db.AuditEvents.ToArrayAsync());
    }

    [Fact]
    public async Task ListInventoryMappings_PreconfiguredProductUsesGlobalPoCatalogWithoutInvoiceCandidate()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var purchaseOrder = NewPurchaseOrder();
        var purchaseOrderLine = Assert.Single(purchaseOrder.Lines);
        purchaseOrderLine.BuyerItemId = "10521531";
        purchaseOrderLine.BuyerItemDescription = "ENERGY BAR PVM BITE SIZE 200G, SBERRY";
        var laterPurchaseOrder = NewPurchaseOrder();
        laterPurchaseOrder.PurchaseOrderNumber = "1210297233";
        laterPurchaseOrder.LastSeenAt = purchaseOrder.LastSeenAt.AddMinutes(1);
        var laterPurchaseOrderLine = Assert.Single(laterPurchaseOrder.Lines);
        laterPurchaseOrderLine.BuyerItemId = purchaseOrderLine.BuyerItemId;
        laterPurchaseOrderLine.BuyerItemDescription = purchaseOrderLine.BuyerItemDescription;
        laterPurchaseOrderLine.Gtin = purchaseOrderLine.Gtin;
        db.ShopritePurchaseOrders.AddRange(purchaseOrder, laterPurchaseOrder);
        db.ShopriteItemMappings.Add(new ShopriteItemMappingEntity
        {
            Id = Guid.NewGuid(),
            AcumaticaInventoryId = "ENER1",
            ShopriteBuyerItemId = "10521531",
            Gtin = purchaseOrderLine.Gtin!,
            IsVerified = true,
            UpdatedBy = "admin@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.ShopriteUomMappings.Add(new ShopriteUomMappingEntity
        {
            Id = Guid.NewGuid(),
            AcumaticaInventoryId = "ENER1",
            AcumaticaUom = "BOX",
            ShopriteUom = ShopriteMeasurementUnit.EA,
            IsVerified = true,
            UpdatedBy = "admin@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var matcher = new ShopriteInvoiceCandidateMatcher(db);
        var service = new ShopriteInventoryMappingService(
            db,
            new ShopriteInvoiceCandidateRevalidationService(db, matcher));

        var view = Assert.Single(await service.ListAsync(null, CancellationToken.None));

        Assert.Equal("ENER1", view.InventoryId);
        Assert.Equal("ENERGY BAR PVM BITE SIZE 200G, SBERRY", view.Description);
        var suggestion = Assert.Single(view.Suggestions);
        Assert.Equal(laterPurchaseOrderLine.Id, suggestion.PurchaseOrderLineId);
        Assert.Equal("10521531", suggestion.ShopriteBuyerItemId);
        Assert.Equal(purchaseOrderLine.Gtin, suggestion.Gtin);
        Assert.Empty(await db.InvoiceCandidates.ToArrayAsync());
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
            Gtin = "06001197040170",
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
                    Uom = "BOX",
                    Gtin = "8581035007071"
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
    public async Task SaveInventoryMapping_PersistsAuditsAndRevalidatesAffectedCandidates()
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
        var secondSource = source with
        {
            Id = "second-acumatica-invoice",
            InvoiceNumber = "INV000124"
        };
        var matcher = new ShopriteInvoiceCandidateMatcher(db);
        var refreshService = new AcumaticaInvoiceCandidateRefreshService(
            new StubInvoiceClient([source, secondSource]),
            db,
            matcher);
        await refreshService.RefreshAsync(CancellationToken.None);
        var candidates = await db.InvoiceCandidates.OrderBy(candidate => candidate.InvoiceNumber).ToArrayAsync();
        Assert.Equal(2, candidates.Length);
        Assert.All(candidates, candidate => Assert.Equal("NeedsReview", candidate.Status));
        var legacyCanonical = Assert.IsType<CanonicalInvoice>(JsonSerializer.Deserialize<CanonicalInvoice>(
            candidates[0].CanonicalJson!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))) with
        {
            AcumaticaInvoiceId = "legacy-acumatica-invoice",
            InvoiceNumber = "INV000125"
        };
        var legacyCandidate = new InvoiceCandidateEntity
        {
            Id = Guid.NewGuid(),
            AcumaticaInvoiceId = "legacy-acumatica-invoice",
            InvoiceNumber = "INV000125",
            CustomerAccount = candidates[0].CustomerAccount,
            ShopritePurchaseOrderNumber = candidates[0].ShopritePurchaseOrderNumber,
            MatchedShopritePurchaseOrderId = candidates[0].MatchedShopritePurchaseOrderId,
            IdempotencyKey = "legacy-idempotency-key",
            Status = "NeedsReview",
            SourceJson = """{"id":"legacy-source"}""",
            CanonicalJson = JsonSerializer.Serialize(
                legacyCanonical,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            ValidationJson = candidates[0].ValidationJson,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.InvoiceCandidates.Add(legacyCandidate);
        await db.SaveChangesAsync();
        candidates = await db.InvoiceCandidates.OrderBy(candidate => candidate.InvoiceNumber).ToArrayAsync();
        var sourceJsonBefore = candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.SourceJson);
        var revalidationService = new ShopriteInvoiceCandidateRevalidationService(db, matcher);
        var mappingService = new ShopriteInventoryMappingService(db, revalidationService);

        var result = await mappingService.SaveAsync(
            inventoryId: "ENER10",
            acumaticaUom: "BOX",
            purchaseOrderLineId: purchaseOrderLine.Id,
            shopriteUom: ShopriteMeasurementUnit.CS,
            actor: "admin@example.com",
            reason: "Verified against Shoprite PO 1215382915.",
            cancellationToken: CancellationToken.None);

        Assert.Equal(ShopriteInventoryMappingSaveStatus.Saved, result.Status);
        Assert.Equal(3, result.RevalidatedCandidateCount);
        candidates = await db.InvoiceCandidates.OrderBy(candidate => candidate.InvoiceNumber).ToArrayAsync();
        Assert.All(candidates, candidate =>
        {
            Assert.Equal("Ready", candidate.Status);
            Assert.Equal(sourceJsonBefore[candidate.Id], candidate.SourceJson);
            var savedCanonical = JsonSerializer.Deserialize<CanonicalInvoice>(
                candidate.CanonicalJson!,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var savedLine = Assert.Single(Assert.IsType<CanonicalInvoice>(savedCanonical).Lines);
            Assert.Equal(purchaseOrderLine.Gtin, savedLine.Gtin);
            Assert.Equal(ShopriteMeasurementUnit.CS, savedLine.ShopriteUom);
            Assert.True(savedLine.IsShopriteUomVerified);
        });
        var itemMapping = await db.ShopriteItemMappings.SingleAsync();
        Assert.Equal("ENER10", itemMapping.AcumaticaInventoryId);
        Assert.Equal("10369734", itemMapping.ShopriteBuyerItemId);
        Assert.Equal("06001197181125", itemMapping.Gtin);
        Assert.True(itemMapping.IsVerified);
        var uomMapping = await db.ShopriteUomMappings.SingleAsync();
        Assert.Equal("BOX", uomMapping.AcumaticaUom);
        Assert.Equal(ShopriteMeasurementUnit.CS, uomMapping.ShopriteUom);
        Assert.True(uomMapping.IsVerified);
        Assert.All(
            await db.AuditEvents.ToListAsync(),
            audit =>
            {
                Assert.Contains("Verified against Shoprite PO 1215382915.", audit.DetailsJson);
                Assert.Contains(purchaseOrderLine.Id.ToString(), audit.DetailsJson);
            });
        var audits = await db.AuditEvents.OrderBy(audit => audit.EntityType).ToArrayAsync();
        Assert.Equal(2, audits.Length);
        Assert.All(audits, audit => Assert.Equal("admin@example.com", audit.Actor));
        var view = Assert.Single(await mappingService.ListAsync("ENER10", CancellationToken.None));
        Assert.Equal("ENER10", view.InventoryId);
        Assert.Equal("BOX", view.AcumaticaUom);
        Assert.Equal(2, view.AffectedCandidateCount);
        Assert.Equal(0, view.UnresolvedCandidateCount);
        Assert.Single(view.ItemMappings);
        Assert.NotNull(view.UomMapping);
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
