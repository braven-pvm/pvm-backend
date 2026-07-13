namespace Pvm.Domain.Invoices;

public sealed record CanonicalInvoice(
    string AcumaticaInvoiceId,
    string InvoiceNumber,
    string CustomerAccount,
    string? CustomerLocation,
    string? ShopritePurchaseOrderNumber,
    string? SupplierGln,
    string? SellerVatRegistrationNumber,
    string? StoreDcGln,
    string CountryCode,
    string CurrencyCode,
    DateTimeOffset InvoiceDate,
    Money TotalExcludingTax,
    Money TotalIncludingTax,
    Money TotalTax,
    IReadOnlyList<CanonicalInvoiceLine> Lines);
