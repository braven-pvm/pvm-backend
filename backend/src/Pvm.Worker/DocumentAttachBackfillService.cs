using Microsoft.Extensions.Options;
using Pvm.Infrastructure.Documents;

namespace Pvm.Worker;

/// <summary>
/// Every 15 minutes, attaches the filed documents whose Acumatica attachment waits (design 6.6):
/// the documents filed in shadow mode, and the ones whose attach failed. Shadow mode does nothing.
/// </summary>
public sealed class DocumentAttachBackfillService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<DocumentCaptureOptions> options,
    ILogger<DocumentAttachBackfillService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            if (options.CurrentValue.AcumaticaAttachMode != AcumaticaAttachMode.Write)
            {
                continue;
            }

            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<DocumentProcessor>();
                await processor.BackfillPendingAttachmentsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "document.attach.backfill.failed; the next run retries.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
