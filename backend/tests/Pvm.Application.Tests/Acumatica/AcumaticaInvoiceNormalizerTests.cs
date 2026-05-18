using Pvm.Application.Acumatica;
using Pvm.Domain.Invoices;

namespace Pvm.Application.Tests.Acumatica;

public sealed class AcumaticaInvoiceNormalizerTests
{
    [Fact]
    public void Normalize_maps_invoice_candidate_to_canonical_invoice()
    {
        var dto = new AcumaticaInvoiceDto(
            Id: "a1",
            InvoiceNumber: "INV001",
            Status: "Released",
            CustomerAccount: "SHOPRITE",
            CustomerLocation: "BRACKENFELL-DC",
            CustomerOrder: "3869384391",
            CurrencyCode: "ZAR",
            CountryCode: "ZA",
            InvoiceDate: new DateTimeOffset(2026, 5, 18, 10, 0, 0, TimeSpan.Zero),
            TotalExcludingTax: 100m,
            TotalIncludingTax: 115m,
            TotalTax: 15m,
            Lines:
            [
                new AcumaticaInvoiceLineDto(
                    LineNumber: 1,
                    InventoryId: "SKU-1",
                    Gtin: "16001069205048",
                    Description: "Item 1",
                    Quantity: 1m,
                    Uom: "EA",
                    PackSize: 1m,
                    UnitAmountExcludingTax: 100m,
                    UnitAmountIncludingTax: 115m,
                    TaxAmount: 15m,
                    TaxCategoryCode: "STANDARD",
                    TaxPercentage: 15m,
                    IsCatchWeight: false)
            ]);

        var invoice = AcumaticaInvoiceNormalizer.Normalize(
            dto,
            supplierGln: "9999999999999",
            storeDcGln: "6001001018104");

        Assert.Equal("a1", invoice.AcumaticaInvoiceId);
        Assert.Equal("INV001", invoice.InvoiceNumber);
        Assert.Equal("SHOPRITE", invoice.CustomerAccount);
        Assert.Equal("BRACKENFELL-DC", invoice.CustomerLocation);
        Assert.Equal("3869384391", invoice.ShopritePurchaseOrderNumber);
        Assert.Equal("9999999999999", invoice.SupplierGln);
        Assert.Equal("6001001018104", invoice.StoreDcGln);
        Assert.Equal("ZA", invoice.CountryCode);
        Assert.Equal("ZAR", invoice.CurrencyCode);
        Assert.Equal(dto.InvoiceDate, invoice.InvoiceDate);
        Assert.Equal(new Money("ZAR", 100m), invoice.TotalExcludingTax);
        Assert.Equal(new Money("ZAR", 115m), invoice.TotalIncludingTax);
        Assert.Equal(new Money("ZAR", 15m), invoice.TotalTax);

        var line = Assert.Single(invoice.Lines);
        Assert.Equal(1, line.LineNumber);
        Assert.Equal("SKU-1", line.AcumaticaInventoryId);
        Assert.Equal("16001069205048", line.Gtin);
        Assert.Equal("Item 1", line.Description);
        Assert.Equal(1m, line.Quantity);
        Assert.Equal("EA", line.AcumaticaUom);
        Assert.Equal(ShopriteMeasurementUnit.EA, line.ShopriteUom);
        Assert.Equal(1m, line.PackSize);
        Assert.Equal(new Money("ZAR", 100m), line.UnitAmountExcludingTax);
        Assert.Equal(new Money("ZAR", 115m), line.UnitAmountIncludingTax);
        Assert.Equal(new Money("ZAR", 15m), line.TaxAmount);
        Assert.Equal("STANDARD", line.TaxCategoryCode);
        Assert.Equal(15m, line.TaxPercentage);
        Assert.False(line.IsCatchWeight);
        Assert.True(line.IsShopriteUomVerified);
    }

    [Fact]
    public void Normalize_sets_shoprite_uom_to_null_when_acumatica_uom_is_unknown()
    {
        var dto = InvoiceWithLineUom("BOX");

        var invoice = AcumaticaInvoiceNormalizer.Normalize(dto, supplierGln: null, storeDcGln: null);

        var line = Assert.Single(invoice.Lines);
        Assert.Null(line.ShopriteUom);
        Assert.True(line.IsShopriteUomVerified);
    }

    private static AcumaticaInvoiceDto InvoiceWithLineUom(string uom)
    {
        return new AcumaticaInvoiceDto(
            Id: "a1",
            InvoiceNumber: "INV001",
            Status: "Released",
            CustomerAccount: "SHOPRITE",
            CustomerLocation: "BRACKENFELL-DC",
            CustomerOrder: "3869384391",
            CurrencyCode: "ZAR",
            CountryCode: "ZA",
            InvoiceDate: new DateTimeOffset(2026, 5, 18, 10, 0, 0, TimeSpan.Zero),
            TotalExcludingTax: 100m,
            TotalIncludingTax: 115m,
            TotalTax: 15m,
            Lines:
            [
                new AcumaticaInvoiceLineDto(1, "SKU-1", "16001069205048", "Item 1", 1m, uom, 1m, 100m, 115m, 15m, "STANDARD", 15m, false)
            ]);
    }
}
