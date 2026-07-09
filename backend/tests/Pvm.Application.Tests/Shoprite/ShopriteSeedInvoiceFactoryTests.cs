using Pvm.Application.Shoprite;
using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;

namespace Pvm.Application.Tests.Shoprite;

public sealed class ShopriteSeedInvoiceFactoryTests
{
    [Fact]
    public void FromPurchaseOrder_CreatesValidQaInvoiceUsingPoContext()
    {
        var purchaseOrder = new SeedShopritePurchaseOrder(
            PurchaseOrderNumber: "1210145512",
            SupplierGln: "6001197000006",
            DeliveryGln: "6001002010109",
            DeliveryLocationCode: "36102",
            DeliveryLocationName: "DC CANELANDS",
            CurrencyCode: "ZAR",
            Lines:
            [
                new SeedShopritePurchaseOrderLine(
                    LineNumber: 1,
                    Gtin: "06001197181125",
                    BuyerItemId: "000123",
                    Description: "Test product",
                    RequestedQuantity: 6m,
                    MeasurementUnitCode: "EA",
                    NetPrice: 100m,
                    MonetaryAmountExcludingTaxes: 600m,
                    MonetaryAmountIncludingTaxes: 690m)
            ]);

        var invoice = ShopriteSeedInvoiceFactory.FromPurchaseOrder(
            purchaseOrder,
            invoiceDate: new DateTimeOffset(2026, 7, 8, 10, 0, 0, TimeSpan.Zero));

        Assert.Equal("QA-SEED-1210145512", invoice.AcumaticaInvoiceId);
        Assert.Equal("QA-INV-1210145512", invoice.InvoiceNumber);
        Assert.Equal("SHOPRITE", invoice.CustomerAccount);
        Assert.Equal("36102", invoice.CustomerLocation);
        Assert.Equal("1210145512", invoice.ShopritePurchaseOrderNumber);
        Assert.Equal("6001197000006", invoice.SupplierGln);
        Assert.Equal("6001002010109", invoice.StoreDcGln);
        Assert.Equal(new Money("ZAR", 600m), invoice.TotalExcludingTax);
        Assert.Equal(new Money("ZAR", 690m), invoice.TotalIncludingTax);
        Assert.Equal(new Money("ZAR", 90m), invoice.TotalTax);

        var line = Assert.Single(invoice.Lines);
        Assert.Equal("06001197181125", line.Gtin);
        Assert.Equal(6m, line.Quantity);
        Assert.Equal(ShopriteMeasurementUnit.EA, line.ShopriteUom);
        Assert.False(line.IsShopriteUomVerified);
        Assert.Equal(new Money("ZAR", 100m), line.UnitAmountExcludingTax);
        Assert.Equal(new Money("ZAR", 115m), line.UnitAmountIncludingTax);
        Assert.Equal(new Money("ZAR", 15m), line.TaxAmount);

        var validation = ShopriteInvoiceValidator.Validate(invoice, ShopriteValidationEnvironment.Qa);
        Assert.True(validation.CanSubmit);
    }

    [Fact]
    public void FromPurchaseOrder_DefaultsMissingMeasurementUnitToUnverifiedEachForQaSeed()
    {
        var purchaseOrder = new SeedShopritePurchaseOrder(
            PurchaseOrderNumber: "1210297232",
            SupplierGln: "6001197000006",
            DeliveryGln: "6001001829106",
            DeliveryLocationCode: "82917",
            DeliveryLocationName: "CHECKERS FX PLETTENBERG BAY MALL",
            CurrencyCode: "ZAR",
            Lines:
            [
                new SeedShopritePurchaseOrderLine(
                    LineNumber: 1,
                    Gtin: "06001197181156",
                    BuyerItemId: "000456",
                    Description: "QA product",
                    RequestedQuantity: 1m,
                    MeasurementUnitCode: null,
                    NetPrice: 226.26m,
                    MonetaryAmountExcludingTaxes: 226.26m,
                    MonetaryAmountIncludingTaxes: 260.20m)
            ]);

        var invoice = ShopriteSeedInvoiceFactory.FromPurchaseOrder(
            purchaseOrder,
            invoiceDate: new DateTimeOffset(2026, 7, 9, 10, 0, 0, TimeSpan.Zero));

        var line = Assert.Single(invoice.Lines);
        Assert.Equal("EA", line.AcumaticaUom);
        Assert.Equal(ShopriteMeasurementUnit.EA, line.ShopriteUom);
        Assert.False(line.IsShopriteUomVerified);

        var validation = ShopriteInvoiceValidator.Validate(invoice, ShopriteValidationEnvironment.Qa);
        Assert.True(validation.CanSubmit);
        Assert.DoesNotContain(validation.Issues, issue => issue.Code == "missing-shoprite-uom");
        Assert.Contains(validation.Issues, issue => issue.Code == "unverified-shoprite-uom");
    }
}
