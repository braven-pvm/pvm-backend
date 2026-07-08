namespace Pvm.Application.Shoprite;

public sealed record ShopritePurchaseOrderBatch(
    string RawPayload,
    IReadOnlyList<ShopritePurchaseOrder> Orders);

