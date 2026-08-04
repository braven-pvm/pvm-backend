using Microsoft.Extensions.Logging;
using Pvm.Application.Messaging;
using Pvm.Application.Shoprite;
using Pvm.Infrastructure.Shoprite;

namespace Pvm.Infrastructure.Operations;

public sealed class ShopritePurchaseOrderRefreshMessageHandler(
    IShopritePurchaseOrderClient purchaseOrderClient,
    ShopritePurchaseOrderRefreshService refreshService,
    ShopriteInvoiceCandidateRevalidationService revalidationService,
    IntegrationRunService runService,
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

        try
        {
            var batch = await purchaseOrderClient.FetchAsync(cancellationToken);
            var result = await refreshService.RefreshAsync(batch, DateTimeOffset.UtcNow, cancellationToken);
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
            await runService.FailAsync(
                runId,
                "shoprite-po-refresh-failed",
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
