using Pvm.Application.Shoprite;
using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;
using Xunit;

namespace Pvm.Application.Tests.Shoprite;

public sealed class ShopriteInvoiceReferenceEnricherTests
{
    private static readonly ShopriteDeliveryLocation GardenWalk =
        new("G980", "6001001698092", "CHECKERS FX GARDEN WALK", ShopriteLocationType.Store, IsVerified: true);

    private static readonly ShopriteDeliveryLocation Riverfields =
        new("G191", "6001001900102", "DC RIVERFIELDS", ShopriteLocationType.DistributionCentre, IsVerified: true);

    private static readonly ShopriteTradeItem CaramelForStores =
        new("ENER12", "BOX", ShopriteLocationType.Store, "06001197181125", 1, 20m, ShopriteMeasurementUnit.EA, IsVerified: true);

    private static readonly ShopriteTradeItem CaramelForDistributionCentres =
        new("ENER12", "BOX", ShopriteLocationType.DistributionCentre, "06001197020509", 12, 240m, ShopriteMeasurementUnit.EA, IsVerified: true);

    [Fact]
    public void A_store_invoice_takes_the_branch_gln_and_the_store_gtin()
    {
        var result = Enrich(Invoice("G980", Line("ENER12", "BOX", 1m, 237.60m)), GardenWalk);

        Assert.Empty(result.Issues);
        Assert.Equal("6001001698092", result.Invoice.StoreDcGln);
        var line = Assert.Single(result.Invoice.Lines);
        Assert.Equal("06001197181125", line.Gtin);
        Assert.Equal(1m, line.Quantity);
        Assert.Equal(ShopriteMeasurementUnit.EA, line.ShopriteUom);
        Assert.Equal(20m, line.PackSize);
        Assert.True(line.IsShopriteUomVerified);
    }

    [Fact]
    public void A_distribution_centre_line_converts_boxes_into_shoprite_cases()
    {
        var result = Enrich(Invoice("G191", Line("ENER12", "BOX", 288m, 237.60m, tax: 35.64m)), Riverfields);

        Assert.Empty(result.Issues);
        Assert.Equal("6001001900102", result.Invoice.StoreDcGln);
        var line = Assert.Single(result.Invoice.Lines);
        Assert.Equal("06001197020509", line.Gtin);
        Assert.Equal(24m, line.Quantity);
        Assert.Equal(2851.20m, line.UnitAmountExcludingTax.Amount);
        Assert.Equal(427.68m, line.TaxAmount.Amount);
        Assert.Equal(240m, line.PackSize);
    }

    [Fact]
    public void The_case_conversion_keeps_the_line_total()
    {
        var source = Line("ENER12", "BOX", 288m, 237.60m);

        var line = Assert.Single(Enrich(Invoice("G191", source), Riverfields).Invoice.Lines);

        Assert.Equal(
            source.Quantity * source.UnitAmountExcludingTax.Amount,
            line.Quantity * line.UnitAmountExcludingTax.Amount);
    }

    [Fact]
    public void A_quantity_that_is_not_whole_cases_blocks_and_is_not_converted()
    {
        var result = Enrich(Invoice("G191", Line("ENER12", "BOX", 100m, 237.60m)), Riverfields);

        var issue = Assert.Single(result.Issues);
        Assert.Equal("quantity-not-whole-shoprite-units", issue.Code);
        Assert.Equal(ValidationSeverity.Blocking, issue.Severity);
        Assert.Equal(100m, Assert.Single(result.Invoice.Lines).Quantity);
    }

    [Fact]
    public void A_customer_with_no_delivery_location_blocks_and_names_the_customer()
    {
        var result = Enrich(Invoice("G999", Line("ENER12", "BOX", 1m, 237.60m)), location: null);

        var issue = Assert.Single(result.Issues);
        Assert.Equal("missing-shoprite-delivery-location", issue.Code);
        Assert.Contains("G999", issue.Message);
        Assert.Null(result.Invoice.StoreDcGln);
    }

    [Fact]
    public void An_unverified_delivery_location_blocks()
    {
        var unverified = GardenWalk with { IsVerified = false };

        var result = Enrich(Invoice("G980", Line("ENER12", "BOX", 1m, 237.60m)), unverified);

        Assert.Contains(result.Issues, issue => issue.Code == "unverified-shoprite-delivery-location");
    }

    [Fact]
    public void An_item_with_no_shoprite_gtin_blocks_and_names_the_item_and_the_delivery_type()
    {
        var result = Enrich(Invoice("G191", Line("ENER99", "BOX", 12m, 237.60m)), Riverfields);

        var issue = Assert.Single(result.Issues);
        Assert.Equal("missing-shoprite-trade-item", issue.Code);
        Assert.Contains("ENER99", issue.Message);
        Assert.Contains("distribution centre", issue.Message);
        Assert.Null(Assert.Single(result.Invoice.Lines).Gtin);
    }

    [Fact]
    public void An_unverified_trade_item_blocks_but_is_still_applied_for_review()
    {
        var items = new[] { CaramelForDistributionCentres with { IsVerified = false } };

        var result = ShopriteInvoiceReferenceEnricher.Enrich(
            Invoice("G191", Line("ENER12", "BOX", 288m, 237.60m)), Riverfields, items, []);

        Assert.Contains(result.Issues, issue => issue.Code == "unverified-shoprite-trade-item");
        var line = Assert.Single(result.Invoice.Lines);
        Assert.Equal("06001197020509", line.Gtin);
        Assert.False(line.IsShopriteUomVerified);
    }

    [Fact]
    public void A_purchase_order_that_already_has_a_submitted_invoice_blocks_and_names_it()
    {
        var result = ShopriteInvoiceReferenceEnricher.Enrich(
            Invoice("G980", Line("ENER12", "BOX", 1m, 237.60m)),
            GardenWalk,
            [CaramelForStores],
            ["INV900001"]);

        var issue = Assert.Single(result.Issues);
        Assert.Equal("shoprite-po-already-invoiced", issue.Code);
        Assert.Contains("INV900001", issue.Message);
    }

    [Fact]
    public void The_supplier_gln_comes_from_the_supplier_profile()
    {
        var result = Enrich(Invoice("G980", Line("ENER12", "BOX", 1m, 237.60m)), GardenWalk);

        Assert.Equal(ShopriteSupplierProfile.PvmSupplierGln, result.Invoice.SupplierGln);
    }

    [Fact]
    public void Lookups_ignore_case_and_surrounding_spaces()
    {
        var result = Enrich(Invoice(" g980 ", Line(" ener12 ", "box", 1m, 237.60m)), GardenWalk);

        Assert.Empty(result.Issues);
        Assert.Equal("06001197181125", Assert.Single(result.Invoice.Lines).Gtin);
    }

    private static ShopriteReferenceEnrichment Enrich(CanonicalInvoice invoice, ShopriteDeliveryLocation? location)
        => ShopriteInvoiceReferenceEnricher.Enrich(
            invoice,
            location,
            [CaramelForStores, CaramelForDistributionCentres],
            []);

    private static CanonicalInvoice Invoice(string customer, params CanonicalInvoiceLine[] lines)
        => new(
            AcumaticaInvoiceId: "INV-1",
            InvoiceNumber: "INV158888",
            CustomerAccount: customer,
            CustomerLocation: "MAIN",
            ShopritePurchaseOrderNumber: "1219763484",
            SupplierGln: null,
            SellerVatRegistrationNumber: null,
            StoreDcGln: null,
            CountryCode: "ZA",
            CurrencyCode: "ZAR",
            InvoiceDate: new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.FromHours(2)),
            TotalExcludingTax: new Money("ZAR", 0m),
            TotalIncludingTax: new Money("ZAR", 0m),
            TotalTax: new Money("ZAR", 0m),
            Lines: lines);

    private static CanonicalInvoiceLine Line(
        string inventoryId,
        string uom,
        decimal quantity,
        decimal unitExcludingTax,
        decimal tax = 0m)
        => new(
            LineNumber: 1,
            AcumaticaInventoryId: inventoryId,
            Gtin: null,
            Description: "E/BAR CARAMEL NUT 20X45g",
            Quantity: quantity,
            AcumaticaUom: uom,
            ShopriteUom: null,
            PackSize: null,
            UnitAmountExcludingTax: new Money("ZAR", unitExcludingTax),
            UnitAmountIncludingTax: new Money("ZAR", unitExcludingTax + tax),
            TaxAmount: new Money("ZAR", tax),
            TaxCategoryCode: "S",
            TaxPercentage: 15m,
            IsCatchWeight: false,
            IsShopriteUomVerified: false);
}
