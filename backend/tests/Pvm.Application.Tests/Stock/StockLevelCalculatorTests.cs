using Pvm.Application.Stock;

namespace Pvm.Application.Tests.Stock;

/// <summary>
/// The figures in these tests are real: the "current stock" sheet of inventory lite.xlsm and the
/// Acumatica reads gave them for HO, location FG on 2026-11-17.
/// </summary>
public sealed class StockLevelCalculatorTests
{
    [Fact]
    public void Held_back_and_auto_adjustments_come_off_on_hand_before_orders()
    {
        // CER07: 3000 came in on a BLADJHO adjustment and 307 on AUTOADJ adjustments since the
        // last count. That stock is not for sale, so the sheet shows -15, not 3292.
        var level = StockLevelCalculator.AcumaticaLevel(new AcumaticaStockFigures(
            "CER07", OnHand: 3307, HeldBackAdjustments: 3000, AutoAdjustments: 307, OpenOrderQuantity: 10, TransfersOut: 5));

        Assert.Equal(-15m, level);
    }

    [Fact]
    public void Stock_left_after_the_adjustments_is_for_sale()
    {
        var level = StockLevelCalculator.AcumaticaLevel(new AcumaticaStockFigures(
            "CER05", OnHand: 2426, HeldBackAdjustments: 2000, AutoAdjustments: 305));

        Assert.Equal(121m, level);
    }

    [Fact]
    public void A_negative_held_back_total_takes_nothing_off()
    {
        // ENER1: the BLADJHO adjustments since May 2022 net to -24. The sheet uses only a
        // positive total.
        var level = StockLevelCalculator.AcumaticaLevel(new AcumaticaStockFigures(
            "ENER1", OnHand: 13780, HeldBackAdjustments: -24, AutoAdjustments: 10, OpenOrderQuantity: 1180, TransfersOut: 5060));

        Assert.Equal(7530m, level);
    }

    [Fact]
    public void On_hand_after_the_adjustments_is_never_below_zero()
    {
        // The floor is on hand only. Orders and transfers still take the level below zero.
        var level = StockLevelCalculator.AcumaticaLevel(new AcumaticaStockFigures(
            "PROT20", OnHand: 10, HeldBackAdjustments: 50, OpenOrderQuantity: 2));

        Assert.Equal(-2m, level);
    }

    [Fact]
    public void Promo_orders_and_transfers_out_come_off_too()
    {
        var level = StockLevelCalculator.AcumaticaLevel(new AcumaticaStockFigures(
            "ENER10", OnHand: 10071, AutoAdjustments: 24, OpenOrderQuantity: 584, PromoOrderQuantity: 24, TransfersOut: 3697));

        Assert.Equal(5742m, level);
    }

    [Fact]
    public void A_warehouse_rule_item_takes_the_larger_of_its_commitments_and_its_orders()
    {
        // PROT21 is outside the sheet's main query: its orders are the larger of the warehouse
        // booked or allocated quantity (12) and the open orders (17).
        var level = StockLevelCalculator.AcumaticaLevel(new AcumaticaStockFigures(
            "PROT21", OnHand: 21, HeldBackAdjustments: 20, AutoAdjustments: 1, OpenOrderQuantity: 17, TransfersOut: 6, WarehouseCommitted: 12));

        Assert.Equal(-23m, level);
    }

    [Fact]
    public void A_warehouse_commitment_larger_than_the_orders_wins()
    {
        var level = StockLevelCalculator.AcumaticaLevel(new AcumaticaStockFigures(
            "PROT25", OnHand: 40, OpenOrderQuantity: 2, WarehouseCommitted: 6));

        Assert.Equal(34m, level);
    }

    [Fact]
    public void Web_orders_not_yet_in_Acumatica_are_pending()
    {
        var pending = StockLevelCalculator.WebPending(
            [new WebOrderLine("5001", "ENER1", 3), new WebOrderLine("5002", "ENER1", 2), new WebOrderLine("5003", "SION10", 1)],
            [new LoadedWebOrderLine("5001", "ENER1", 3)]);

        Assert.Equal(2m, pending["ENER1"]);
        Assert.Equal(1m, pending["SION10"]);
    }

    [Fact]
    public void Pending_is_never_negative()
    {
        var pending = StockLevelCalculator.WebPending(
            [new WebOrderLine("5001", "ENER1", 1)],
            [new LoadedWebOrderLine("5001", "ENER1", 4)]);

        Assert.False(pending.ContainsKey("ENER1"));
    }

    [Theory]
    [InlineData(4.9, 0)]
    [InlineData(5, 0)]
    [InlineData(5.9, 0)]
    [InlineData(6, 6)]
    [InlineData(121.7, 121)]
    [InlineData(-15, 0)]
    public void Published_quantity_is_zero_at_or_below_the_out_of_stock_level(double level, int expected)
        => Assert.Equal(expected, StockLevelCalculator.PublishedQuantity((decimal)level, 5));
}
