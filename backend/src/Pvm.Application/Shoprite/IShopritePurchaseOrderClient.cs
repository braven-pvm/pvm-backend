namespace Pvm.Application.Shoprite;

public interface IShopritePurchaseOrderClient
{
    Task<ShopritePurchaseOrderBatch> FetchAsync(CancellationToken cancellationToken);
}

