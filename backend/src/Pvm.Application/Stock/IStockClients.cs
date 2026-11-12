namespace Pvm.Application.Stock;

/// <summary>Reads stock figures from Acumatica over the contract REST API.</summary>
public interface IAcumaticaStockReader
{
    /// <param name="inventoryCds">The codes the web store sells. Only visible ones are read.</param>
    /// <param name="webOrderIds">Web-store order ids still in progress, to find those Acumatica already holds.</param>
    Task<AcumaticaStockSnapshot> ReadAsync(
        IReadOnlyCollection<string> inventoryCds,
        IReadOnlyCollection<string> webOrderIds,
        CancellationToken cancellationToken);
}

/// <summary>Reads and sets stock in the WooCommerce web store.</summary>
public interface IWooCommerceStockClient
{
    /// <summary>Every product and variation that carries a SKU.</summary>
    Task<IReadOnlyList<WebStockItem>> ListStockItemsAsync(CancellationToken cancellationToken);

    /// <summary>Lines of orders the store has taken but not completed.</summary>
    Task<IReadOnlyList<WebOrderLine>> ListPendingOrderLinesAsync(CancellationToken cancellationToken);

    Task SetStockQuantityAsync(WebStockItem item, int quantity, CancellationToken cancellationToken);
}
