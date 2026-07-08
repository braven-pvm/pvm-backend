namespace Pvm.Application.Shoprite;

public sealed record ShopritePurchaseOrderLine(
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

