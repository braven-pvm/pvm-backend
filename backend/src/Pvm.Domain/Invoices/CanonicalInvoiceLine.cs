namespace Pvm.Domain.Invoices;

public sealed record CanonicalInvoiceLine(
    int LineNumber,
    string AcumaticaInventoryId,
    string? Gtin,
    string Description,
    decimal Quantity,
    string AcumaticaUom,
    ShopriteMeasurementUnit? ShopriteUom,
    decimal? PackSize,
    Money UnitAmountExcludingTax,
    Money UnitAmountIncludingTax,
    Money TaxAmount,
    string? TaxCategoryCode,
    decimal? TaxPercentage,
    bool IsCatchWeight);
