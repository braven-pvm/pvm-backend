using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pvm.Application.Messaging;
using Pvm.Application.Submissions;
using Pvm.Infrastructure.Acumatica;
using Pvm.Infrastructure.Documents;
using Pvm.Infrastructure.Operations;

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
    ShopritePurchaseOrderRefreshMessageHandler purchaseOrderRefreshHandler,
    AcumaticaInvoiceReconciliationMessageHandler acumaticaReconciliationHandler,
    AcumaticaInvoiceCandidateRefreshService acumaticaRefreshService,
    SubmitShopriteInvoiceHandler submitHandler,
    DocumentProcessor documentProcessor,
    ILogger<IntegrationMessageProcessor>? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<IntegrationMessageProcessor>.Instance;

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

        if (delivery.Status is "DeadLettered" or "DeadLetterResolved")
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
            await OnFinalFailureAsync(envelope, cancellationToken);
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
            await OnFinalFailureAsync(envelope, cancellationToken);
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
                var refreshCommand = envelope.Data.Deserialize<RefreshShopritePurchaseOrdersMessage>(SerializerOptions)
                    ?? throw new JsonException("Shoprite refresh data is required.");
                await purchaseOrderRefreshHandler.HandleAsync(envelope, refreshCommand, cancellationToken);
                return null;

            case IntegrationMessageTypes.AcumaticaInvoiceDiscoveryV1:
                _ = envelope.Data.Deserialize<DiscoverAcumaticaInvoicesMessage>(SerializerOptions)
                    ?? throw new JsonException("Acumatica discovery data is required.");
                await acumaticaRefreshService.RefreshAsync(cancellationToken);
                return null;

            case IntegrationMessageTypes.AcumaticaInvoiceReconciliationV1:
                var reconciliationCommand = envelope.Data.Deserialize<ReconcileAcumaticaInvoicesMessage>(SerializerOptions)
                    ?? throw new JsonException("Acumatica reconciliation data is required.");
                await acumaticaReconciliationHandler.HandleAsync(
                    envelope,
                    reconciliationCommand,
                    cancellationToken);
                return null;

            case IntegrationMessageTypes.AcumaticaInvoiceChangedV1:
                var changedInvoice = envelope.Data.Deserialize<DiscoverAcumaticaInvoiceMessage>(SerializerOptions)
                    ?? throw new JsonException("Acumatica changed-invoice data is required.");
                if (string.IsNullOrWhiteSpace(changedInvoice.InvoiceId))
                {
                    throw new JsonException("Acumatica changed-invoice ID is required.");
                }

                await acumaticaRefreshService.RefreshInvoiceAsync(
                    changedInvoice.InvoiceId,
                    cancellationToken);
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
                    SubmitShopriteInvoiceStatus.PolicyBlocked => null,
                    SubmitShopriteInvoiceStatus.ValidationBlocked => ("validation-blocked", result.Message),
                    SubmitShopriteInvoiceStatus.ManualReviewRequired => ("manual-review-required", result.Message),
                    SubmitShopriteInvoiceStatus.Failed => throw new InvalidOperationException(result.Message),
                    _ => throw new InvalidOperationException("Unknown submission result.")
                };

            case IntegrationMessageTypes.DocumentReceivedV1:
                var document = envelope.Data.Deserialize<ProcessCapturedDocumentMessage>(SerializerOptions)
                    ?? throw new JsonException("Captured document data is required.");
                if (document.DocumentId == Guid.Empty)
                {
                    throw new JsonException("Captured document ID is required.");
                }

                try
                {
                    await documentProcessor.ProcessAsync(document.DocumentId, cancellationToken);
                }
                catch (JsonException exception)
                {
                    // Only the payload above is message data. A JsonException from the processing
                    // itself (for example a non-JSON reply from Acumatica) is transient: retry it.
                    throw new InvalidOperationException(
                        $"Document processing failed: {exception.Message}",
                        exception);
                }

                return null;

            default:
                return ("unknown-message-type", $"Message type '{envelope.MessageType}' is not supported.");
        }
    }

    /// <summary>
    /// Runs before a handler failure dead-letters the message, so the work item the message
    /// carried does not stay in progress forever. A failure here is logged and swallowed: the
    /// message is still dead-lettered, and the row keeps its last state (Received or Processing)
    /// for a person to find.
    /// </summary>
    private async Task OnFinalFailureAsync(IntegrationMessageEnvelope envelope, CancellationToken cancellationToken)
    {
        if (envelope.MessageType != IntegrationMessageTypes.DocumentReceivedV1)
        {
            return;
        }

        Guid documentId;
        try
        {
            documentId = envelope.Data.Deserialize<ProcessCapturedDocumentMessage>(SerializerOptions)?.DocumentId
                ?? Guid.Empty;
        }
        catch (JsonException)
        {
            return;
        }

        if (documentId == Guid.Empty)
        {
            return;
        }

        try
        {
            await documentProcessor.MarkFailedAsync(documentId, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                exception,
                "document.process.mark-failed.failed DocumentId={DocumentId}; the message is dead-lettered anyway.",
                documentId);
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
        (IntegrationQueues.AcumaticaInvoiceDiscovery, IntegrationMessageTypes.AcumaticaInvoiceReconciliationV1) => true,
        (IntegrationQueues.AcumaticaInvoiceDiscovery, IntegrationMessageTypes.AcumaticaInvoiceChangedV1) => true,
        (IntegrationQueues.ShopriteInvoiceSubmit, IntegrationMessageTypes.ShopriteInvoiceSubmitV1) => true,
        (IntegrationQueues.DocumentProcessing, IntegrationMessageTypes.DocumentReceivedV1) => true,
        _ => false
    };
}
