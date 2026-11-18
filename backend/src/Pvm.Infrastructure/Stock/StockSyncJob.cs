using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pvm.Infrastructure.Stock;

/// <summary>
/// One pass of the scheduled stock job: run the sync and record it. A run that did not finish
/// (an error, or a write that failed partway) is recorded with what it did, then throws, so that
/// the Container Apps job reports a failed replica.
/// </summary>
public sealed class StockSyncJob(
    StockSyncService sync,
    StockSyncRunRecorder recorder,
    IOptions<StockSyncOptions> options,
    TimeProvider timeProvider,
    ILogger<StockSyncJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();
        StockSyncResult result;
        try
        {
            result = await sync.RunAsync(cancellationToken);
            await recorder.RecordAsync(result, startedAt, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "integration.run.failed RunType=woocommerce-stock-sync");
            try
            {
                await recorder.RecordFailureAsync(options.Value.Mode, startedAt, exception, CancellationToken.None);
            }
            catch (Exception recordException)
            {
                logger.LogError(recordException, "stock.sync.record.failed");
            }

            throw;
        }

        logger.LogInformation(
            "integration.run.completed RunType=woocommerce-stock-sync Mode={Mode} Items={Items} Updates={Updates} Unchanged={Unchanged} Skipped={Skipped} Written={Written} Refusal={Refusal}",
            result.Mode,
            result.Lines.Count,
            result.Updates,
            result.Unchanged,
            result.Skipped,
            result.Written,
            result.Refusal ?? string.Empty);

        if (result.Failure is not null)
        {
            throw new InvalidOperationException(result.Failure);
        }
    }
}
