using System.Text.Json;
using Pvm.Application.Messaging;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Messaging;

public sealed class IntegrationCommandQueue(PvmDbContext dbContext) : IIntegrationCommandQueue
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<Guid> EnqueueAsync<T>(
        string queueName,
        string messageType,
        T data,
        string correlationId,
        string? causationId = null,
        Guid? messageId = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var id = messageId ?? Guid.NewGuid();
        var dataElement = JsonSerializer.SerializeToElement(data, SerializerOptions);
        var envelope = new IntegrationMessageEnvelope(id, messageType, correlationId, causationId, now, dataElement);

        dbContext.IntegrationOutboxMessages.Add(new IntegrationOutboxMessageEntity
        {
            Id = id,
            QueueName = queueName,
            MessageType = messageType,
            PayloadJson = JsonSerializer.Serialize(envelope, SerializerOptions),
            CorrelationId = correlationId,
            CausationId = causationId,
            AvailableAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return id;
    }
}
