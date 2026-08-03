using Azure.Messaging.ServiceBus;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Messaging;

public interface IIntegrationMessagePublisher
{
    Task PublishAsync(IntegrationOutboxMessageEntity message, CancellationToken cancellationToken);
}

public sealed class ServiceBusMessagePublisher(
    ServiceBusClient client) : IIntegrationMessagePublisher, IAsyncDisposable
{
    private readonly Dictionary<string, ServiceBusSender> _senders = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    public async Task PublishAsync(IntegrationOutboxMessageEntity message, CancellationToken cancellationToken)
    {
        var brokerMessage = new ServiceBusMessage(message.PayloadJson)
        {
            MessageId = message.Id.ToString("D"),
            CorrelationId = message.CorrelationId,
            ContentType = "application/json",
            Subject = message.MessageType
        };
        brokerMessage.ApplicationProperties["messageType"] = message.MessageType;
        if (!string.IsNullOrWhiteSpace(message.CausationId))
        {
            brokerMessage.ApplicationProperties["causationId"] = message.CausationId;
        }

        await GetSender(message.QueueName).SendMessageAsync(brokerMessage, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var sender in _senders.Values)
        {
            await sender.DisposeAsync();
        }
    }

    private ServiceBusSender GetSender(string queueName)
    {
        lock (_sync)
        {
            if (!_senders.TryGetValue(queueName, out var sender))
            {
                sender = client.CreateSender(queueName);
                _senders.Add(queueName, sender);
            }

            return sender;
        }
    }
}
