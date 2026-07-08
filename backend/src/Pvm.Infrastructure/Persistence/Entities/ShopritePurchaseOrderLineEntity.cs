namespace Pvm.Infrastructure.Persistence.Entities;

public sealed class ShopritePurchaseOrderLineEntity
{
    public Guid Id { get; set; }
    public Guid ShopritePurchaseOrderId { get; set; }
    public int LineNumber { get; set; }
    public string? Gtin { get; set; }
    public string? BuyerItemId { get; set; }
    public string? BuyerItemDescription { get; set; }
    public string? SupplierItemId { get; set; }
    public string? Description { get; set; }
    public decimal? RequestedQuantity { get; set; }
    public string? MeasurementUnitCode { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal? NetPrice { get; set; }
    public decimal? MonetaryAmountExcludingTaxes { get; set; }
    public decimal? MonetaryAmountIncludingTaxes { get; set; }
}

