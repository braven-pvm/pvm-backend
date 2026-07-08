namespace Pvm.Application.Shoprite;

public sealed record ShopritePurchaseOrder(
    string PurchaseOrderNumber,
    string? OrderHeaderId,
    string? OrderTypeCode,
    string? OrderTypeLabel,
    string? SupplierGln,
    string? BuyerGln,
    string? DeliveryGln,
    string? DeliveryLocationCode,
    string? DeliveryLocationName,
    string DeliveryLocationSource,
    string? CurrencyCode,
    decimal? TotalExcludingTax,
    decimal? TotalIncludingTax,
    decimal? TotalTax,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastUpdatedAt,
    string RawOrderJson,
    IReadOnlyList<ShopritePurchaseOrderLine> Lines);

