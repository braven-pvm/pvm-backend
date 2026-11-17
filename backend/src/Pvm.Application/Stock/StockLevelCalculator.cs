namespace Pvm.Application.Stock;

/// <summary>
/// The stock arithmetic of the "current stock" sheet in inventory lite.xlsm, the figure the
/// business trusts. Verified against the sheet's own queries for every web product on 2026-11-17.
/// The legacy update stock.py left out the held-back and automatic adjustments; this does not.
/// </summary>
public static class StockLevelCalculator
{
    /// <summary>
    /// Stock that can be promised, as the sheet's Nett Stock:
    /// on hand less the held-back adjustments (when their net is positive) and the automatic
    /// adjustments since the last count, never below zero; then less the open orders, the promo
    /// orders and the transfers out. For a warehouse-rule item the orders are the larger of the
    /// warehouse commitment and the open orders.
    /// </summary>
    public static decimal AcumaticaLevel(AcumaticaStockFigures figures)
    {
        ArgumentNullException.ThrowIfNull(figures);

        var forSale = Math.Max(
            0,
            figures.OnHand - Math.Max(0, figures.HeldBackAdjustments) - figures.AutoAdjustments);
        var orders = figures.WarehouseCommitted is { } committed
            ? Math.Max(committed, figures.OpenOrderQuantity)
            : figures.OpenOrderQuantity;
        return forSale - orders - figures.PromoOrderQuantity - figures.TransfersOut;
    }

    /// <summary>
    /// Web-store order quantity that Acumatica does not hold yet, per SKU. An order that Acumatica
    /// already has is counted there, so its lines are taken back out. Never negative: a mismatch
    /// between the two systems must not add stock.
    /// </summary>
    public static IReadOnlyDictionary<string, decimal> WebPending(
        IEnumerable<WebOrderLine> webOrderLines,
        IEnumerable<LoadedWebOrderLine> loadedLines)
    {
        ArgumentNullException.ThrowIfNull(webOrderLines);
        ArgumentNullException.ThrowIfNull(loadedLines);

        var pending = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in webOrderLines)
        {
            pending[line.Sku] = pending.GetValueOrDefault(line.Sku) + line.Quantity;
        }

        foreach (var line in loadedLines)
        {
            if (pending.TryGetValue(line.InventoryCd, out var quantity))
            {
                pending[line.InventoryCd] = quantity - line.Quantity;
            }
        }

        return pending
            .Where(entry => entry.Value > 0)
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The quantity shown to customers. At or below the out-of-stock level the store shows none,
    /// so that a handful of units does not take orders the warehouse cannot fill.
    /// </summary>
    public static int PublishedQuantity(decimal level, int outOfStockAtOrBelow)
    {
        var whole = (int)Math.Floor(Math.Max(0, level));
        return whole <= outOfStockAtOrBelow ? 0 : whole;
    }
}
