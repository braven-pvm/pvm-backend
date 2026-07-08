using Pvm.Domain.Invoices;

namespace Pvm.Application.Shoprite;

public static class ShopriteSeedInvoiceFactory
{
    public static CanonicalInvoice FromPurchaseOrder(
        SeedShopritePurchaseOrder purchaseOrder,
        DateTimeOffset invoiceDate)
    {
        var currencyCode = string.IsNullOrWhiteSpace(purchaseOrder.CurrencyCode)
            ? "ZAR"
            : purchaseOrder.CurrencyCode;
        var lines = purchaseOrder.Lines
            .OrderBy(line => line.LineNumber)
            .Select(line => ToInvoiceLine(line, currencyCode))
            .ToArray();
        var totalExcludingTax = lines.Sum(line => RoundCurrency(line.UnitAmountExcludingTax.Amount * line.Quantity));
        var totalIncludingTax = lines.Sum(line => RoundCurrency(line.UnitAmountIncludingTax.Amount * line.Quantity));
        var totalTax = lines.Sum(line => RoundCurrency(line.TaxAmount.Amount * line.Quantity));

        return new CanonicalInvoice(
            AcumaticaInvoiceId: $"QA-SEED-{purchaseOrder.PurchaseOrderNumber}",
            InvoiceNumber: $"QA-INV-{purchaseOrder.PurchaseOrderNumber}",
            CustomerAccount: "SHOPRITE",
            CustomerLocation: purchaseOrder.DeliveryLocationCode,
            ShopritePurchaseOrderNumber: purchaseOrder.PurchaseOrderNumber,
            SupplierGln: purchaseOrder.SupplierGln,
            StoreDcGln: purchaseOrder.DeliveryGln,
            CountryCode: "ZA",
            CurrencyCode: currencyCode,
            InvoiceDate: invoiceDate,
            TotalExcludingTax: new Money(currencyCode, totalExcludingTax),
            TotalIncludingTax: new Money(currencyCode, totalIncludingTax),
            TotalTax: new Money(currencyCode, totalTax),
            Lines: lines);
    }

    private static CanonicalInvoiceLine ToInvoiceLine(
        SeedShopritePurchaseOrderLine line,
        string currencyCode)
    {
        var quantity = line.RequestedQuantity is > 0 ? line.RequestedQuantity.Value : 1m;
        var excludingTax = line.MonetaryAmountExcludingTaxes
            ?? RoundCurrency((line.NetPrice ?? 1m) * quantity);
        var includingTax = line.MonetaryAmountIncludingTaxes
            ?? RoundCurrency(excludingTax * 1.15m);
        var tax = RoundCurrency(includingTax - excludingTax);
        var taxPercentage = excludingTax == 0m
            ? 0m
            : RoundCurrency(tax / excludingTax * 100m);

        return new CanonicalInvoiceLine(
            LineNumber: line.LineNumber,
            AcumaticaInventoryId: FirstNonBlank(line.SupplierItemId, line.BuyerItemId, line.Gtin, $"PO-LINE-{line.LineNumber}")!,
            Gtin: line.Gtin,
            Description: FirstNonBlank(line.Description, line.BuyerItemDescription, line.BuyerItemId, line.Gtin, $"PO line {line.LineNumber}")!,
            Quantity: quantity,
            AcumaticaUom: FirstNonBlank(line.MeasurementUnitCode, "EA")!,
            ShopriteUom: ParseMeasurementUnit(line.MeasurementUnitCode),
            PackSize: null,
            UnitAmountExcludingTax: new Money(currencyCode, RoundCurrency(excludingTax / quantity)),
            UnitAmountIncludingTax: new Money(currencyCode, RoundCurrency(includingTax / quantity)),
            TaxAmount: new Money(currencyCode, RoundCurrency(tax / quantity)),
            TaxCategoryCode: tax > 0m ? "STANDARD" : "ZERO",
            TaxPercentage: tax > 0m ? taxPercentage : 0m,
            IsCatchWeight: false,
            IsShopriteUomVerified: false);
    }

    private static ShopriteMeasurementUnit? ParseMeasurementUnit(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : Enum.TryParse<ShopriteMeasurementUnit>(value.Trim(), ignoreCase: true, out var unit)
                ? unit
                : null;

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static decimal RoundCurrency(decimal value)
        => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed record SeedShopritePurchaseOrder(
    string PurchaseOrderNumber,
    string? SupplierGln,
    string? DeliveryGln,
    string? DeliveryLocationCode,
    string? DeliveryLocationName,
    string? CurrencyCode,
    IReadOnlyList<SeedShopritePurchaseOrderLine> Lines);

public sealed record SeedShopritePurchaseOrderLine(
    int LineNumber,
    string? Gtin,
    string? BuyerItemId,
    string? Description,
    decimal? RequestedQuantity,
    string? MeasurementUnitCode,
    decimal? NetPrice,
    decimal? MonetaryAmountExcludingTaxes,
    decimal? MonetaryAmountIncludingTaxes,
    string? SupplierItemId = null,
    string? BuyerItemDescription = null);
