using System.Text.Json;
using Pvm.Application.Messaging;
using Pvm.Application.Shoprite;
using Pvm.Application.Submissions;
using Pvm.Infrastructure.Acumatica;
using Pvm.Infrastructure.Shoprite;

namespace Pvm.Infrastructure.Messaging;

public enum MessageSettlement
{
    Complete,
    Abandon,
    DeadLetter
}

public sealed record MessageProcessingResult(
    MessageSettlement Settlement,
    string? DeadLetterReason = null,
    string? ErrorSummary = null);

public sealed class IntegrationMessageProcessor(
    IntegrationDeliveryRepository deliveryRepository,
    IShopritePurchaseOrderClient purchaseOrderClient,
    ShopritePurchaseOrderRefreshService purchaseOrderRefreshService,
    AcumaticaInvoiceCandidateRefreshService acumaticaRefreshService,
    SubmitShopriteInvoiceHandler submitHandler)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<MessageProcessingResult> ProcessAsync(
        string queueName,
        string brokerMessageId,
        string payload,
        int deliveryCount,
        int maxDeliveryCount,
        DateTimeOffset enqueuedAt,
        CancellationToken cancellationToken)
    {
        IntegrationMessageEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<IntegrationMessageEnvelope>(payload, SerializerOptions);
        }
        catch (JsonException exception)
        {
            return await DeadLetterMalformedAsync(queueName, brokerMessageId, deliveryCount, enqueuedAt,
                "invalid-json", exception.Message, cancellationToken);
        }

        if (envelope is null || envelope.MessageId == Guid.Empty || string.IsNullOrWhiteSpace(envelope.MessageType))
        {
            return await DeadLetterMalformedAsync(queueName, brokerMessageId, deliveryCount, enqueuedAt,
                "invalid-envelope", "The message envelope is incomplete.", cancellationToken);
        }

        var delivery = await deliveryRepository.RecordReceivedAsync(
            queueName,
            brokerMessageId,
            envelope.MessageType,
            envelope.CorrelationId,
            envelope.CausationId,
            deliveryCount,
            enqueuedAt,
            cancellationToken);

        if (delivery.Status == "Completed")
        {
            return new MessageProcessingResult(MessageSettlement.Complete);
        }

        if (delivery.Status == "DeadLettered")
        {
            return new MessageProcessingResult(
                MessageSettlement.DeadLetter,
                delivery.DeadLetterReason ?? "previously-dead-lettered",
                delivery.ErrorSummary);
        }

        if (!string.Equals(brokerMessageId, envelope.MessageId.ToString("D"), StringComparison.OrdinalIgnoreCase))
        {
            return await DeadLetterAsync(
                delivery.Id,
                "message-id-mismatch",
                "The broker message ID does not match the persisted envelope ID.",
                cancellationToken);
        }

        if (!IsExpectedMessage(queueName, envelope.MessageType))
        {
            return await DeadLetterAsync(delivery.Id, "message-contract-mismatch",
                $"Message type '{envelope.MessageType}' is not valid for queue '{queueName}'.",
                cancellationToken);
        }

        try
        {
            var terminal = await DispatchAsync(envelope, cancellationToken);
            if (terminal is not null)
            {
                return await DeadLetterAsync(delivery.Id, terminal.Value.Code, terminal.Value.Summary, cancellationToken);
            }

            await deliveryRepository.MarkCompletedAsync(delivery.Id, cancellationToken);
            return new MessageProcessingResult(MessageSettlement.Complete);
        }
        catch (JsonException exception)
        {
            return await DeadLetterAsync(delivery.Id, "invalid-message-data", exception.Message, cancellationToken);
        }
        catch (Exception exception) when (deliveryCount < maxDeliveryCount)
        {
            await deliveryRepository.MarkRetryingAsync(
                delivery.Id,
                "handler-failed",
                exception.Message,
                cancellationToken);
            return new MessageProcessingResult(MessageSettlement.Abandon, ErrorSummary: exception.Message);
        }
        catch (Exception exception)
        {
            return await DeadLetterAsync(delivery.Id, "max-delivery-count",
                $"Handler failed after {deliveryCount} deliveries: {exception.Message}", cancellationToken);
        }
    }

    private async Task<(string Code, string Summary)?> DispatchAsync(
        IntegrationMessageEnvelope envelope,
        CancellationToken cancellationToken)
    {
        switch (envelope.MessageType)
        {
            case IntegrationMessageTypes.ShopritePurchaseOrderRefreshV1:
                _ = envelope.Data.Deserialize<RefreshShopritePurchaseOrdersMessage>(SerializerOptions)
                    ?? throw new JsonException("Shoprite refresh data is required.");
                var batch = await purchaseOrderClient.FetchAsync(cancellationToken);
                await purchaseOrderRefreshService.RefreshAsync(batch, DateTimeOffset.UtcNow, cancellationToken);
                return null;

            case IntegrationMessageTypes.AcumaticaInvoiceDiscoveryV1:
                _ = envelope.Data.Deserialize<DiscoverAcumaticaInvoicesMessage>(SerializerOptions)
                    ?? throw new JsonException("Acumatica discovery data is required.");
                await acumaticaRefreshService.RefreshAsync(cancellationToken);
                return null;

            case IntegrationMessageTypes.ShopriteInvoiceSubmitV1:
                var command = envelope.Data.Deserialize<SubmitShopriteInvoiceMessage>(SerializerOptions)
                    ?? throw new JsonException("Shoprite submission data is required.");
                var result = await submitHandler.HandleAsync(
                    new SubmitShopriteInvoiceCommand(
                        command.CommandId,
                        command.CandidateId,
                        command.RequestedBy,
                        command.Mode),
                    cancellationToken);
                return result.Status switch
                {
                    SubmitShopriteInvoiceStatus.Submitted or
                    SubmitShopriteInvoiceStatus.InProgress or
                    SubmitShopriteInvoiceStatus.DuplicateBlocked or
                    SubmitShopriteInvoiceStatus.Ambiguous => null,
                    SubmitShopriteInvoiceStatus.ValidationBlocked => ("validation-blocked", result.Message),
                    SubmitShopriteInvoiceStatus.ManualReviewRequired => ("manual-review-required", result.Message),
                    SubmitShopriteInvoiceStatus.Failed => throw new InvalidOperationException(result.Message),
                    _ => throw new InvalidOperationException("Unknown submission result.")
                };

            default:
                return ("unknown-message-type", $"Message type '{envelope.MessageType}' is not supported.");
        }
    }

    private async Task<MessageProcessingResult> DeadLetterMalformedAsync(
        string queueName,
        string messageId,
        int deliveryCount,
        DateTimeOffset enqueuedAt,
        string code,
        string summary,
        CancellationToken cancellationToken)
    {
        var delivery = await deliveryRepository.RecordReceivedAsync(
            queueName, messageId, "unknown", messageId, null, deliveryCount, enqueuedAt, cancellationToken);
        return await DeadLetterAsync(delivery.Id, code, summary, cancellationToken);
    }

    private async Task<MessageProcessingResult> DeadLetterAsync(
        Guid deliveryId,
        string code,
        string summary,
        CancellationToken cancellationToken)
    {
        await deliveryRepository.MarkDeadLetteredAsync(deliveryId, code, summary, code, cancellationToken);
        return new MessageProcessingResult(MessageSettlement.DeadLetter, code, summary);
    }

    private static bool IsExpectedMessage(string queueName, string messageType) => (queueName, messageType) switch
    {
        (IntegrationQueues.ShopritePurchaseOrderRefresh, IntegrationMessageTypes.ShopritePurchaseOrderRefreshV1) => true,
        (IntegrationQueues.AcumaticaInvoiceDiscovery, IntegrationMessageTypes.AcumaticaInvoiceDiscoveryV1) => true,
        (IntegrationQueues.ShopriteInvoiceSubmit, IntegrationMessageTypes.ShopriteInvoiceSubmitV1) => true,
        _ => false
    };
}
