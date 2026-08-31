namespace Pvm.Api.Features.ShopritePurchaseOrders;

public sealed record PurchaseOrderRefreshResponse(
    Guid RunId,
    Guid MessageId,
    bool Created,
    string StatusUrl);

public sealed record PurchaseOrderSummaryResponse(
    Guid Id,
    string PurchaseOrderNumber,
    string? OrderTypeCode,
    string? OrderTypeLabel,
    string? DeliveryGln,
    string? DeliveryLocationCode,
    string? DeliveryLocationName,
    string DeliveryLocationSource,
    string? SupplierGln,
    int LineCount,
    DateTimeOffset LastSeenAt);

public sealed record PurchaseOrderDetailResponse(
    Guid Id,
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
    string SourceEnvironment,
    string SourceEndpoint,
    string? PayloadHash,
    DateTimeOffset? ShopriteCreatedAt,
    DateTimeOffset? ShopriteLastUpdatedAt,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? AcknowledgedAt,
    int AcknowledgementAttempts,
    string? LastAcknowledgementError,
    IReadOnlyList<PurchaseOrderLineResponse> Lines,
    IReadOnlyList<LinkedInvoiceCandidateResponse> LinkedInvoiceCandidates,
    string? RawOrderJson);

public sealed record PurchaseOrderLineResponse(
    Guid Id,
    int LineNumber,
    string? Gtin,
    string? BuyerItemId,
    string? BuyerItemDescription,
    string? SupplierItemId,
    string? Description,
    decimal? RequestedQuantity,
    string? MeasurementUnitCode,
    decimal? NetAmount,
    decimal? NetPrice,
    decimal? MonetaryAmountExcludingTaxes,
    decimal? MonetaryAmountIncludingTaxes);

public sealed record LinkedInvoiceCandidateResponse(
    Guid Id,
    string InvoiceNumber,
    string CustomerAccount,
    string Status,
    DateTimeOffset UpdatedAt);


public sealed record ResetPurchaseOrdersRequest(string[]? PurchaseOrderNumbers, string Reason);
