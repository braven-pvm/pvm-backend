namespace Pvm.Application.Shoprite;

public interface IShopritePurchaseOrderClient
{
    Task<ShopritePurchaseOrderBatch> FetchAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Acknowledges orders that were downloaded and stored. Shoprite returns the same orders,
    /// and stops providing new ones, until acknowledgement succeeds.
    /// </summary>
    Task AcknowledgeAsync(IReadOnlyCollection<string> purchaseOrderNumbers, CancellationToken cancellationToken);

    /// <summary>
    /// Resets orders so Shoprite offers them for download again. Support action only.
    /// </summary>
    Task ResetAsync(IReadOnlyCollection<string> purchaseOrderNumbers, CancellationToken cancellationToken);
}
