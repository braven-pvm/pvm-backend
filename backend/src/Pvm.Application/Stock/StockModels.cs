namespace Pvm.Application.Stock;

/// <summary>
/// Acumatica figures for one item, as the "current stock" sheet of inventory lite.xlsm uses them
/// for the warehouse and location the web store sells from. Quantities are as Acumatica holds
/// them; <see cref="StockLevelCalculator"/> applies the signs.
/// </summary>
/// <param name="OnHand">On hand at the location.</param>
/// <param name="HeldBackAdjustments">
/// Net of the adjustments with the held-back reason code (BLADJHO) at the warehouse since the
/// cut-off date, adjustments on hold included and stock-take adjustments left out. Stock that came
/// in this way is not for sale.
/// </param>
/// <param name="AutoAdjustments">
/// Net of the automatic adjustments (AUTOADJ) at the warehouse after the item's last stock count.
/// </param>
/// <param name="OpenOrderQuantity">Recent sales orders from the warehouse with no confirmed shipment.</param>
/// <param name="PromoOrderQuantity">Orders of the promotion accounts with no confirmed shipment.</param>
/// <param name="TransfersOut">Unreleased transfers out of the location.</param>
/// <param name="WarehouseCommitted">
/// For items outside the sheet's main query (PROT, SUPL, REPL): the larger of the warehouse booked
/// and allocated quantities. Null for every other item.
/// </param>
public sealed record AcumaticaStockFigures(
    string InventoryCd,
    decimal OnHand,
    decimal HeldBackAdjustments = 0,
    decimal AutoAdjustments = 0,
    decimal OpenOrderQuantity = 0,
    decimal PromoOrderQuantity = 0,
    decimal TransfersOut = 0,
    decimal? WarehouseCommitted = null);

/// <summary>An order line from Acumatica whose customer order number is a web-store order id.</summary>
public sealed record LoadedWebOrderLine(string WebOrderId, string InventoryCd, decimal Quantity);

/// <summary>Everything one run reads from Acumatica.</summary>
/// <param name="Visible">Inventory codes the integration user can see. A code outside this set is unknown, not zero.</param>
/// <param name="Figures">Figures for each visible code that was asked for and read successfully.</param>
/// <param name="Failures">Codes that are visible but could not be read, with the reason.</param>
/// <param name="LoadedWebOrderLines">Lines of web-store orders that Acumatica already holds.</param>
public sealed record AcumaticaStockSnapshot(
    IReadOnlySet<string> Visible,
    IReadOnlyDictionary<string, AcumaticaStockFigures> Figures,
    IReadOnlyDictionary<string, string> Failures,
    IReadOnlyList<LoadedWebOrderLine> LoadedWebOrderLines);

/// <summary>How the web store manages stock for one product or variation.</summary>
public enum WebStockManagement
{
    /// <summary>The item keeps its own stock quantity, which the sync can set.</summary>
    Own,

    /// <summary>A variation that takes its stock from the parent product.</summary>
    Parent,

    /// <summary>The store does not track stock for this item.</summary>
    None,
}

/// <summary>One product or variation in the web store that carries a SKU.</summary>
public sealed record WebStockItem(
    string Sku,
    long ProductId,
    long? VariationId,
    WebStockManagement Management,
    int? CurrentQuantity);

/// <summary>A line of a web-store order that is not yet complete.</summary>
public sealed record WebOrderLine(string WebOrderId, string Sku, decimal Quantity);

public enum StockSyncAction
{
    Update,
    Unchanged,
    Skip,
}

/// <summary>What one run decides for one web-store item.</summary>
public sealed record StockSyncLine(
    WebStockItem Item,
    StockSyncAction Action,
    int? NewQuantity,
    decimal? AcumaticaLevel,
    decimal WebPending,
    string? Reason,
    AcumaticaStockFigures? Figures = null);
