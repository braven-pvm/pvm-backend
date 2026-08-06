using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pvm.Application.Acumatica;
using Pvm.Application.Messaging;
using Pvm.Infrastructure.Acumatica;

namespace Pvm.Infrastructure.Operations;

public sealed class AcumaticaInvoiceReconciliationMessageHandler(
    AcumaticaInvoiceCandidateRefreshService refreshService,
    IntegrationRunService runService,
    IOptions<AcumaticaOptions> acumaticaOptions,
    IOptions<AcumaticaReconciliationOptions> reconciliationOptions,
    ILogger<AcumaticaInvoiceReconciliationMessageHandler> logger)
{
    public async Task HandleAsync(
        IntegrationMessageEnvelope envelope,
        ReconcileAcumaticaInvoicesMessage command,
        CancellationToken cancellationToken)
    {
        var options = reconciliationOptions.Value;
        var cursorBefore = await runService.GetLatestSuccessfulAcumaticaCursorAsync(
            command.QueryTo,
            cancellationToken);
        var invoiceFloor = acumaticaOptions.Value.InvoiceDateFrom
            ?? command.QueryTo.AddDays(-options.DailyLookbackDays);
        var requestedFrom = command.LookbackDays is > 0
            ? command.QueryTo.AddDays(-command.LookbackDays.Value)
            : cursorBefore?.AddMinutes(-options.OverlapMinutes)
                ?? command.QueryTo.AddDays(-options.DailyLookbackDays);
        var queryFrom = requestedFrom > invoiceFloor ? requestedFrom : invoiceFloor;

        if (queryFrom >= command.QueryTo)
        {
            throw new InvalidOperationException("The Acumatica reconciliation window has no positive duration.");
        }

        await runService.StartAcumaticaInvoiceReconciliationAsync(
            command.RunId,
            envelope.MessageId,
            envelope.CorrelationId,
            command,
            cursorBefore,
            queryFrom,
            cancellationToken);

        try
        {
            var result = await refreshService.RefreshAsync(
                new AcumaticaInvoiceQuery(queryFrom, command.QueryTo),
                cancellationToken);
            await runService.CompleteAcumaticaInvoiceReconciliationAsync(
                command.RunId,
                command.QueryTo,
                result,
                cancellationToken);
            logger.LogInformation(
                "integration.run.completed RunType={RunType} RunId={RunId} Received={Received} Created={Created} Updated={Updated} Unchanged={Unchanged} CursorAfter={CursorAfter}",
                IntegrationRunTypes.AcumaticaInvoiceReconciliation,
                command.RunId,
                result.Received,
                result.Created,
                result.Updated,
                result.Unchanged,
                command.QueryTo);
        }
        catch (Exception exception)
        {
            await runService.FailAsync(
                command.RunId,
                "acumatica-reconciliation-failed",
                exception.Message,
                CancellationToken.None);
            logger.LogError(
                exception,
                "integration.run.failed RunType={RunType} RunId={RunId} ErrorCode={ErrorCode}",
                IntegrationRunTypes.AcumaticaInvoiceReconciliation,
                command.RunId,
                "acumatica-reconciliation-failed");
            throw;
        }
    }
}
