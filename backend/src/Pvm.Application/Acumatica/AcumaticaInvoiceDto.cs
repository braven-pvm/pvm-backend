namespace Pvm.Application.Acumatica;

public sealed record AcumaticaInvoiceDto(
    string Id,
    string InvoiceNumber,
    string Status,
    string CustomerAccount,
    string? CustomerLocation,
    string? CustomerOrder,
    string CurrencyCode,
    string CountryCode,
    DateTimeOffset InvoiceDate,
    decimal TotalExcludingTax,
    decimal TotalIncludingTax,
    decimal TotalTax,
    IReadOnlyList<AcumaticaInvoiceLineDto> Lines);

public sealed record AcumaticaInvoiceLineDto(
    int LineNumber,
    string InventoryId,
    string? Gtin,
    string Description,
    decimal Quantity,
    string Uom,
    decimal? PackSize,
    decimal UnitAmountExcludingTax,
    decimal UnitAmountIncludingTax,
    decimal TaxAmount,
    string? TaxCategoryCode,
    decimal? TaxPercentage,
    bool IsCatchWeight);
