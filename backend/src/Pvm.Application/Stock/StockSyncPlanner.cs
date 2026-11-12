namespace Pvm.Application.Stock;

/// <summary>
/// Decides, for every web-store item, whether its stock quantity changes. It never turns a
/// missing or unreadable figure into zero: an item it cannot account for is skipped with a reason.
/// </summary>
public static class StockSyncPlanner
{
    public static IReadOnlyList<StockSyncLine> Plan(
        IReadOnlyList<WebStockItem> webItems,
        AcumaticaStockSnapshot snapshot,
        IReadOnlyDictionary<string, decimal> webPending,
        int minimumPublished)
    {
        ArgumentNullException.ThrowIfNull(webItems);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(webPending);

        var lines = new List<StockSyncLine>(webItems.Count);
        foreach (var item in webItems)
        {
            var pending = webPending.GetValueOrDefault(item.Sku);
            lines.Add(PlanOne(item, snapshot, pending, minimumPublished));
        }

        return lines;
    }

    private static StockSyncLine PlanOne(
        WebStockItem item,
        AcumaticaStockSnapshot snapshot,
        decimal pending,
        int minimumPublished)
    {
        if (item.Management == WebStockManagement.None)
        {
            return Skip(item, pending, "the web store does not track stock for this item");
        }

        if (item.Management == WebStockManagement.Parent)
        {
            return Skip(item, pending, "this variation takes its stock from the parent product");
        }

        if (!snapshot.Visible.Contains(item.Sku))
        {
            return Skip(item, pending, "not visible to the integration user in Acumatica (missing, or hidden by a restriction group)");
        }

        if (snapshot.Failures.TryGetValue(item.Sku, out var failure))
        {
            return Skip(item, pending, $"the Acumatica figures could not be read: {failure}");
        }

        if (!snapshot.Figures.TryGetValue(item.Sku, out var figures))
        {
            return Skip(item, pending, "Acumatica returned no figures for this item");
        }

        var level = StockLevelCalculator.AcumaticaLevel(figures) - pending;
        var quantity = StockLevelCalculator.PublishedQuantity(level, minimumPublished);
        var action = quantity == item.CurrentQuantity ? StockSyncAction.Unchanged : StockSyncAction.Update;
        return new StockSyncLine(item, action, quantity, level, pending, null);
    }

    private static StockSyncLine Skip(WebStockItem item, decimal pending, string reason)
        => new(item, StockSyncAction.Skip, null, null, pending, reason);
}
