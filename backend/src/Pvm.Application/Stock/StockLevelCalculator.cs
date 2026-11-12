namespace Pvm.Application.Stock;

/// <summary>
/// The stock arithmetic the web store has always used, moved off direct SQL. Verified against the
/// legacy <c>stock.sql</c> and <c>stock_orders.sql</c> for every mapped product on 2026-11-11.
/// </summary>
public static class StockLevelCalculator
{
    /// <summary>
    /// Stock that can be promised. Orders take whichever is larger: what is booked and allocated
    /// at the location, or every recent order that has not shipped yet. Unreleased transfers move
    /// stock in or out before they are released, so they count now.
    /// </summary>
    public static decimal AcumaticaLevel(AcumaticaStockFigures figures)
    {
        ArgumentNullException.ThrowIfNull(figures);

        var committed = Math.Max(
            figures.SalesOrderBooked + figures.SalesOrderAllocated,
            figures.OpenOrderQuantity);
        return figures.OnHand - committed + figures.TransfersIn - figures.TransfersOut;
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
    /// The quantity shown to customers. Below the minimum, the store shows none, so that a
    /// handful of units does not take orders the warehouse cannot fill.
    /// </summary>
    public static int PublishedQuantity(decimal level, int minimumPublished)
    {
        if (level < minimumPublished || level <= 0)
        {
            return 0;
        }

        return (int)Math.Floor(level);
    }
}
