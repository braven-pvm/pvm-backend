using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pvm.Application.Messaging;
using Pvm.Application.Shoprite;
using Pvm.Infrastructure.Shoprite;

namespace Pvm.Infrastructure.Operations;

public sealed class ShopritePurchaseOrderRefreshMessageHandler(
    IShopritePurchaseOrderClient purchaseOrderClient,
    ShopritePurchaseOrderRefreshService refreshService,
    ShopriteOrderAcknowledgementService acknowledgementService,
    ShopriteInvoiceCandidateRevalidationService revalidationService,
    IntegrationRunService runService,
    IOptions<ShopritePurchaseOrderRefreshOptions> options,
    ILogger<ShopritePurchaseOrderRefreshMessageHandler> logger)
{
    public async Task HandleAsync(
        IntegrationMessageEnvelope envelope,
        RefreshShopritePurchaseOrdersMessage command,
        CancellationToken cancellationToken)
    {
        var runId = command.RunId ?? envelope.MessageId;
        await runService.StartShopritePoRefreshAsync(
            runId,
            envelope.MessageId,
            envelope.CorrelationId,
            command,
            cancellationToken);

        // The single place that reads Shoprite orders refuses unless reading is switched on, so
        // neither a schedule, a queued message, nor a person can take orders off the portal.
        if (!options.Value.Enabled)
        {
            await runService.FailAsync(
                runId,
                "shoprite-po-refresh-switched-off",
                "Reading Shoprite orders is switched off. Shoprite marks an order as downloaded when it is read, "
                + "which removes it from the people who work orders on the Shoprite portal.",
                CancellationToken.None);
            logger.LogWarning(
                "shoprite.po.refresh.refused RunId={RunId} Reason=switched-off",
                runId);
            return;
        }

        try
        {
            var batch = await purchaseOrderClient.FetchAsync(cancellationToken);
            var result = await refreshService.RefreshAsync(batch, DateTimeOffset.UtcNow, cancellationToken);
            var fetchedOrderNumbers = batch.Orders
                .Select(order => order.PurchaseOrderNumber)
                .Where(orderNumber => !string.IsNullOrWhiteSpace(orderNumber))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var acknowledgement = await acknowledgementService.AcknowledgeFetchedOrdersAsync(
                fetchedOrderNumbers,
                DateTimeOffset.UtcNow,
                cancellationToken);
            if (acknowledgement.Error is not null)
            {
                logger.LogWarning(
                    "shoprite.order.acknowledgement.failed RunId={RunId} Pending={Pending} Error={Error}",
                    runId,
                    acknowledgement.Pending,
                    acknowledgement.Error);
            }
            else if (acknowledgement.Enabled)
            {
                logger.LogInformation(
                    "shoprite.order.acknowledgement.completed RunId={RunId} Acknowledged={Acknowledged} Pending={Pending}",
                    runId,
                    acknowledgement.Acknowledged,
                    acknowledgement.Pending);
            }

            var revalidated = await revalidationService.RevalidateForPurchaseOrdersAsync(
                result.ChangedPurchaseOrderNumbers,
                DateTimeOffset.UtcNow,
                cancellationToken);
            await runService.CompleteShopritePoRefreshAsync(runId, result, revalidated, cancellationToken);
            logger.LogInformation(
                "integration.run.completed RunType={RunType} RunId={RunId} Status={Status} Received={Received} Created={Created} Updated={Updated} Unchanged={Unchanged} Revalidated={Revalidated}",
                IntegrationRunTypes.ShopritePurchaseOrderRefresh,
                runId,
                IntegrationRunStatuses.Succeeded,
                result.Received,
                result.Created,
                result.Updated,
                result.Unchanged,
                revalidated);
        }
        catch (Exception exception)
        {
            var errorCode = exception is IntegrationCredentialsRejectedException
                ? "shoprite-credentials-rejected"
                : "shoprite-po-refresh-failed";
            if (exception is IntegrationCredentialsRejectedException rejected)
            {
                logger.LogError(
                    IntegrationCredentialsRejectedException.LogEvent + " System={System} StatusCode={StatusCode} RunId={RunId}",
                    rejected.System,
                    rejected.StatusCode,
                    runId);
            }

            await runService.FailAsync(
                runId,
                errorCode,
                exception.Message,
                CancellationToken.None);
            logger.LogError(
                exception,
                "integration.run.failed RunType={RunType} RunId={RunId} Status={Status}",
                IntegrationRunTypes.ShopritePurchaseOrderRefresh,
                runId,
                IntegrationRunStatuses.Failed);
            throw;
        }
    }
}
