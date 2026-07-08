namespace Pvm.Infrastructure.Persistence.Entities;

public sealed class ShopritePurchaseOrderEntity
{
    public Guid Id { get; set; }
    public required string PurchaseOrderNumber { get; set; }
    public string? OrderHeaderId { get; set; }
    public string? OrderTypeCode { get; set; }
    public string? OrderTypeLabel { get; set; }
    public string? SupplierGln { get; set; }
    public string? BuyerGln { get; set; }
    public string? DeliveryGln { get; set; }
    public string? DeliveryLocationCode { get; set; }
    public string? DeliveryLocationName { get; set; }
    public required string DeliveryLocationSource { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal? TotalExcludingTax { get; set; }
    public decimal? TotalIncludingTax { get; set; }
    public decimal? TotalTax { get; set; }
    public string SourceEnvironment { get; set; } = "qa";
    public string SourceEndpoint { get; set; } = "VendorOrder";
    public string? PayloadHash { get; set; }
    public string? RawOrderJson { get; set; }
    public DateTimeOffset? ShopriteCreatedAt { get; set; }
    public DateTimeOffset? ShopriteLastUpdatedAt { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public List<ShopritePurchaseOrderLineEntity> Lines { get; set; } = [];
}

