using Pvm.Application.Stock;
using Xunit;

namespace Pvm.Application.Tests.Stock;

public class StockLevelCalculatorTests
{
    private static AcumaticaStockFigures Figures(
        decimal onHand,
        decimal booked = 0,
        decimal allocated = 0,
        decimal openOrders = 0,
        decimal transfersIn = 0,
        decimal transfersOut = 0)
        => new("ITEM", onHand, booked, allocated, openOrders, transfersIn, transfersOut);

    [Fact]
    public void Allocated_orders_and_outgoing_transfers_reduce_stock()
    {
        // ENER10 as read on 2026-09-28: the legacy SQL produced 6520.
        var level = StockLevelCalculator.AcumaticaLevel(Figures(8856, allocated: 500, transfersOut: 1836));

        Assert.Equal(6520m, level);
    }

    [Fact]
    public void A_booked_order_counts_alongside_allocated_ones()
    {
        // DYNA24 on 2026-11-11: one booked order, two units leaving on a transfer. Legacy: 12.
        var level = StockLevelCalculator.AcumaticaLevel(Figures(15, booked: 1, transfersOut: 2));

        Assert.Equal(12m, level);
    }

    [Fact]
    public void Recent_unshipped_orders_win_when_they_exceed_what_is_booked()
    {
        var level = StockLevelCalculator.AcumaticaLevel(Figures(40, booked: 1, allocated: 1, openOrders: 10));

        Assert.Equal(30m, level);
    }

    [Fact]
    public void Booked_and_allocated_win_when_they_exceed_recent_unshipped_orders()
    {
        var level = StockLevelCalculator.AcumaticaLevel(Figures(40, booked: 3, allocated: 5, openOrders: 2));

        Assert.Equal(32m, level);
    }

    [Fact]
    public void Incoming_transfers_add_stock()
    {
        var level = StockLevelCalculator.AcumaticaLevel(Figures(10, transfersIn: 4, transfersOut: 1));

        Assert.Equal(13m, level);
    }

    [Theory]
    [InlineData(4.9, 5, 0)]
    [InlineData(5, 5, 5)]
    [InlineData(3408, 5, 3408)]
    [InlineData(12.7, 5, 12)]
    [InlineData(-3, 5, 0)]
    [InlineData(0, 0, 0)]
    public void Published_quantity_hides_small_and_negative_levels(decimal level, int minimum, int expected)
    {
        Assert.Equal(expected, StockLevelCalculator.PublishedQuantity(level, minimum));
    }

    [Fact]
    public void Web_orders_count_until_Acumatica_holds_them()
    {
        var web = new[]
        {
            new WebOrderLine("1001", "ENER1", 2),
            new WebOrderLine("1002", "ENER1", 3),
            new WebOrderLine("1002", "DYNA20", 1),
        };
        var loaded = new[] { new LoadedWebOrderLine("1002", "ENER1", 3) };

        var pending = StockLevelCalculator.WebPending(web, loaded);

        Assert.Equal(2m, pending["ENER1"]);
        Assert.Equal(1m, pending["DYNA20"]);
    }

    [Fact]
    public void Web_pending_is_never_negative()
    {
        var web = new[] { new WebOrderLine("1001", "ENER1", 2) };
        var loaded = new[] { new LoadedWebOrderLine("1001", "ENER1", 5) };

        var pending = StockLevelCalculator.WebPending(web, loaded);

        Assert.False(pending.ContainsKey("ENER1"));
    }
}
