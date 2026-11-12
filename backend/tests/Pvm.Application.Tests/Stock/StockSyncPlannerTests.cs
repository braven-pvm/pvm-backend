using Pvm.Application.Stock;
using Xunit;

namespace Pvm.Application.Tests.Stock;

public class StockSyncPlannerTests
{
    private static WebStockItem Item(
        string sku,
        int? current,
        WebStockManagement management = WebStockManagement.Own)
        => new(sku, 100, 200, management, current);

    private static AcumaticaStockSnapshot Snapshot(
        IEnumerable<AcumaticaStockFigures> figures,
        IEnumerable<string>? visible = null,
        IDictionary<string, string>? failures = null)
    {
        var list = figures.ToList();
        return new AcumaticaStockSnapshot(
            new HashSet<string>(visible ?? list.Select(f => f.InventoryCd), StringComparer.OrdinalIgnoreCase),
            list.ToDictionary(f => f.InventoryCd, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(failures ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
            []);
    }

    private static AcumaticaStockFigures Figures(string sku, decimal onHand) => new(sku, onHand, 0, 0, 0, 0, 0);

    private static readonly Dictionary<string, decimal> NoPending = new(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void A_changed_level_is_an_update()
    {
        var line = Assert.Single(StockSyncPlanner.Plan([Item("ENER12", 3000)], Snapshot([Figures("ENER12", 3408)]), NoPending, 5));

        Assert.Equal(StockSyncAction.Update, line.Action);
        Assert.Equal(3408, line.NewQuantity);
    }

    [Fact]
    public void An_equal_level_is_unchanged()
    {
        var line = Assert.Single(StockSyncPlanner.Plan([Item("ENER12", 3408)], Snapshot([Figures("ENER12", 3408)]), NoPending, 5));

        Assert.Equal(StockSyncAction.Unchanged, line.Action);
    }

    [Fact]
    public void An_item_hidden_from_the_integration_user_is_skipped_not_zeroed()
    {
        // DYNA32 sits in a restriction group. Publishing 0 would mark it sold out.
        var line = Assert.Single(StockSyncPlanner.Plan(
            [Item("DYNA32", 31)],
            Snapshot([], visible: []),
            NoPending,
            5));

        Assert.Equal(StockSyncAction.Skip, line.Action);
        Assert.Null(line.NewQuantity);
        Assert.Contains("not visible", line.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unreadable_item_is_skipped_not_zeroed()
    {
        var line = Assert.Single(StockSyncPlanner.Plan(
            [Item("ENER1", 400)],
            Snapshot([], visible: ["ENER1"], failures: new Dictionary<string, string> { ["ENER1"] = "HTTP 500" }),
            NoPending,
            5));

        Assert.Equal(StockSyncAction.Skip, line.Action);
        Assert.Null(line.NewQuantity);
    }

    [Theory]
    [InlineData(WebStockManagement.Parent)]
    [InlineData(WebStockManagement.None)]
    public void Items_whose_stock_the_store_does_not_hold_are_skipped(WebStockManagement management)
    {
        var line = Assert.Single(StockSyncPlanner.Plan(
            [Item("ENER1", 458, management)],
            Snapshot([Figures("ENER1", 7000)]),
            NoPending,
            5));

        Assert.Equal(StockSyncAction.Skip, line.Action);
    }

    [Fact]
    public void Pending_web_orders_reduce_the_published_quantity()
    {
        var pending = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["SION10"] = 3 };

        var line = Assert.Single(StockSyncPlanner.Plan([Item("SION10", 14)], Snapshot([Figures("SION10", 14)]), pending, 5));

        Assert.Equal(11, line.NewQuantity);
        Assert.Equal(3m, line.WebPending);
    }

    [Fact]
    public void A_level_below_the_minimum_publishes_zero()
    {
        var line = Assert.Single(StockSyncPlanner.Plan([Item("SION11", 6)], Snapshot([Figures("SION11", 4)]), NoPending, 5));

        Assert.Equal(StockSyncAction.Update, line.Action);
        Assert.Equal(0, line.NewQuantity);
    }
}
