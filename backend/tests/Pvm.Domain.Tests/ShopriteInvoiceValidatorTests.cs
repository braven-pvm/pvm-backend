using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;

namespace Pvm.Domain.Tests;

public sealed class ShopriteInvoiceValidatorTests
{
    [Fact]
    public void Missing_purchase_order_blocks_submission()
    {
        var invoice = ValidInvoice() with { ShopritePurchaseOrderNumber = null };

        var result = ShopriteInvoiceValidator.Validate(invoice, ShopriteValidationEnvironment.Qa);

        AssertBlocking(result, "missing-shoprite-po");
    }

    [Fact]
    public void Missing_store_dc_gln_blocks_submission()
    {
        var invoice = ValidInvoice() with { StoreDcGln = null };

        var result = ShopriteInvoiceValidator.Validate(invoice, ShopriteValidationEnvironment.Qa);

        AssertBlocking(result, "missing-store-dc-gln");
    }

    [Fact]
    public void Non_zar_currency_blocks_submission()
    {
        var invoice = ValidInvoice() with
        {
            CurrencyCode = "USD",
            TotalExcludingTax = new Money("USD", 100m),
            TotalIncludingTax = new Money("USD", 115m),
            TotalTax = new Money("USD", 15m),
            Lines =
            [
                ValidLine() with
                {
                    UnitAmountExcludingTax = new Money("USD", 100m),
                    UnitAmountIncludingTax = new Money("USD", 115m),
                    TaxAmount = new Money("USD", 15m)
                }
            ]
        };

        var result = ShopriteInvoiceValidator.Validate(invoice, ShopriteValidationEnvironment.Qa);

        AssertBlocking(result, "unsupported-currency");
    }

    [Fact]
    public void Catch_weight_line_blocks_submission()
    {
        var invoice = ValidInvoice() with { Lines = [ValidLine() with { IsCatchWeight = true }] };

        var result = ShopriteInvoiceValidator.Validate(invoice, ShopriteValidationEnvironment.Qa);

        AssertBlocking(result, "catch-weight-unsupported");
    }

    [Fact]
    public void Unverified_uom_is_warning_in_qa()
    {
        var invoice = ValidInvoice() with { Lines = [ValidLine() with { IsShopriteUomVerified = false }] };

        var result = ShopriteInvoiceValidator.Validate(invoice, ShopriteValidationEnvironment.Qa);

        Assert.Contains(
            result.Issues,
            issue => issue.Code == "unverified-shoprite-uom" && issue.Severity == ValidationSeverity.Warning);
        Assert.True(result.CanSubmit);
    }

    [Fact]
    public void Unverified_uom_blocks_submission_in_production()
    {
        var invoice = ValidInvoice() with { Lines = [ValidLine() with { IsShopriteUomVerified = false }] };

        var result = ShopriteInvoiceValidator.Validate(invoice, ShopriteValidationEnvironment.Production);

        AssertBlocking(result, "unverified-shoprite-uom");
    }

    [Fact]
    public void Missing_shoprite_uom_blocks_submission()
    {
        var invoice = ValidInvoice() with { Lines = [ValidLine() with { ShopriteUom = null }] };

        var result = ShopriteInvoiceValidator.Validate(invoice, ShopriteValidationEnvironment.Qa);

        AssertBlocking(result, "missing-shoprite-uom");
    }

    [Fact]
    public void Zero_quantity_line_blocks_submission()
    {
        var invoice = ValidInvoice() with { Lines = [ValidLine() with { Quantity = 0m }] };

        var result = ShopriteInvoiceValidator.Validate(invoice, ShopriteValidationEnvironment.Qa);

        AssertBlocking(result, "zero-quantity-line");
    }

    [Fact]
    public void Totals_mismatch_blocks_submission()
    {
        var invoice = ValidInvoice() with { TotalIncludingTax = new Money("ZAR", 116m) };

        var result = ShopriteInvoiceValidator.Validate(invoice, ShopriteValidationEnvironment.Qa);

        AssertBlocking(result, "totals-mismatch");
    }

    private static void AssertBlocking(ValidationResult result, string code)
    {
        Assert.Contains(
            result.Issues,
            issue => issue.Code == code && issue.Severity == ValidationSeverity.Blocking);
        Assert.False(result.CanSubmit);
    }

    private static CanonicalInvoice ValidInvoice()
    {
        return new CanonicalInvoice(
            AcumaticaInvoiceId: "INV-1",
            InvoiceNumber: "INV0001",
            CustomerAccount: "SHOPRITE",
            CustomerLocation: "DC-01",
            ShopritePurchaseOrderNumber: "3869384391",
            SupplierGln: "9999999999999",
            StoreDcGln: "6001001018104",
            CountryCode: "ZA",
            CurrencyCode: "ZAR",
            InvoiceDate: new DateTimeOffset(2026, 5, 18, 10, 0, 0, TimeSpan.Zero),
            TotalExcludingTax: new Money("ZAR", 100m),
            TotalIncludingTax: new Money("ZAR", 115m),
            TotalTax: new Money("ZAR", 15m),
            Lines: [ValidLine()]);
    }

    private static CanonicalInvoiceLine ValidLine()
    {
        return new CanonicalInvoiceLine(
            LineNumber: 1,
            AcumaticaInventoryId: "SKU-1",
            Gtin: "16001069205048",
            Description: "Item 1",
            Quantity: 1m,
            AcumaticaUom: "EA",
            ShopriteUom: ShopriteMeasurementUnit.EA,
            PackSize: 1m,
            UnitAmountExcludingTax: new Money("ZAR", 100m),
            UnitAmountIncludingTax: new Money("ZAR", 115m),
            TaxAmount: new Money("ZAR", 15m),
            TaxCategoryCode: "STANDARD",
            TaxPercentage: 15m,
            IsCatchWeight: false);
    }
}
