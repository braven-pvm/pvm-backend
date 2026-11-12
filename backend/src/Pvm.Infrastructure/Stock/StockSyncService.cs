using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pvm.Application.Stock;

namespace Pvm.Infrastructure.Stock;

/// <summary>The outcome of one stock-sync run.</summary>
public sealed record StockSyncResult(
    StockSyncMode Mode,
    IReadOnlyList<StockSyncLine> Lines,
    int Written,
    string? Refusal,
    string? Failure = null)
{
    public int Updates => Lines.Count(line => line.Action == StockSyncAction.Update);

    public int Unchanged => Lines.Count(line => line.Action == StockSyncAction.Unchanged);

    public int Skipped => Lines.Count(line => line.Action == StockSyncAction.Skip);
}

/// <summary>
/// One run of the Acumatica → WooCommerce stock sync. Any read that fails stops the run before
/// it writes, because a partial picture of stock is worse than a stale one.
/// </summary>
public sealed class StockSyncService(
    IWooCommerceStockClient webStore,
    IAcumaticaStockReader acumatica,
    IOptions<StockSyncOptions> options,
    ILogger<StockSyncService> logger)
{
    private readonly StockSyncOptions _options = options.Value;

    public async Task<StockSyncResult> RunAsync(CancellationToken cancellationToken)
    {
        if (_options.Mode == StockSyncMode.Disabled)
        {
            return new StockSyncResult(_options.Mode, [], 0, "the stock sync is disabled");
        }

        var items = await webStore.ListStockItemsAsync(cancellationToken);
        var pendingLines = await webStore.ListPendingOrderLinesAsync(cancellationToken);
        var snapshot = await acumatica.ReadAsync(
            items.Select(item => item.Sku).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            pendingLines.Select(line => line.WebOrderId).Distinct(StringComparer.Ordinal).ToList(),
            cancellationToken);

        var pending = StockLevelCalculator.WebPending(pendingLines, snapshot.LoadedWebOrderLines);
        var lines = StockSyncPlanner.Plan(items, snapshot, pending, _options.MinimumPublishedQuantity);
        foreach (var line in lines)
        {
            Log(line);
        }

        if (_options.Mode != StockSyncMode.Write)
        {
            return new StockSyncResult(_options.Mode, lines, 0, null);
        }

        var zeroed = lines.Count(line => line.Action == StockSyncAction.Update
            && line.NewQuantity == 0
            && line.Item.CurrentQuantity > 0);
        if (zeroed > _options.MaximumItemsZeroedPerRun)
        {
            var refusal = $"{zeroed} in-stock items would drop to zero, above the limit of {_options.MaximumItemsZeroedPerRun}";
            logger.LogError("stock.sync.refused Reason={Reason}", refusal);
            return new StockSyncResult(_options.Mode, lines, 0, refusal);
        }

        var updates = lines.Where(line => line.Action == StockSyncAction.Update).ToList();
        var written = 0;
        foreach (var line in updates)
        {
            try
            {
                await webStore.SetStockQuantityAsync(line.Item, line.NewQuantity!.Value, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Earlier writes are already live in the web store, so the result must say how many.
                var failure = $"The write for {line.Item.Sku} failed after {written} of {updates.Count} updates: {exception.Message}";
                logger.LogError(exception, "stock.sync.write.failed Sku={Sku} Written={Written}", line.Item.Sku, written);
                return new StockSyncResult(_options.Mode, lines, written, null, failure);
            }

            written++;
        }

        return new StockSyncResult(_options.Mode, lines, written, null);
    }

    private void Log(StockSyncLine line)
        => logger.LogInformation(
            "stock.sync.item Sku={Sku} Action={Action} Current={Current} New={New} AcumaticaLevel={AcumaticaLevel} WebPending={WebPending} Reason={Reason}",
            line.Item.Sku,
            line.Action,
            line.Item.CurrentQuantity,
            line.NewQuantity,
            line.AcumaticaLevel,
            line.WebPending,
            line.Reason ?? string.Empty);
}
