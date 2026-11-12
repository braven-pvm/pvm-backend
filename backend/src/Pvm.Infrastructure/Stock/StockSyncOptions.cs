namespace Pvm.Infrastructure.Stock;

/// <summary>Whether a run changes the web store.</summary>
public enum StockSyncMode
{
    /// <summary>The job does nothing.</summary>
    Disabled,

    /// <summary>The job reads both systems and logs every decision, but writes nothing.</summary>
    Shadow,

    /// <summary>The job sets stock quantities in the web store.</summary>
    Write,
}

/// <summary>
/// Configuration for the Acumatica → WooCommerce stock sync. The defaults reproduce the legacy
/// <c>update stock.py</c>: warehouse HO, location FG, sales orders of type SO and S1 placed in the
/// last 45 days, a published minimum of 5.
/// </summary>
public sealed record StockSyncOptions
{
    public const string SectionName = "StockSync";

    public StockSyncMode Mode { get; init; } = StockSyncMode.Shadow;

    /// <summary>The Acumatica warehouse the web store sells from.</summary>
    public string WarehouseId { get; init; } = "HO";

    /// <summary>The location inside that warehouse.</summary>
    public string LocationId { get; init; } = "FG";

    /// <summary>Below this level the store shows no stock.</summary>
    public int MinimumPublishedQuantity { get; init; } = 5;

    /// <summary>How far back an unshipped sales order still holds stock.</summary>
    public int OpenOrderLookbackDays { get; init; } = 45;

    public string[] OpenOrderTypes { get; init; } = ["SO", "S1"];

    /// <summary>
    /// Customers whose orders do not hold stock. The legacy script kept this list inside its SQL.
    /// </summary>
    public string[] ExcludedCustomers { get; init; } = [];

    /// <summary>The UTC offset Acumatica stores order dates in. South Africa has no daylight saving.</summary>
    public string AcumaticaUtcOffset { get; init; } = "+02:00";

    /// <summary>
    /// A write run refuses to start when it would set more than this many in-stock items to zero.
    /// A wide drop to zero means something was misread, not that stock ran out everywhere at once.
    /// </summary>
    public int MaximumItemsZeroedPerRun { get; init; } = 10;
}
