using Microsoft.EntityFrameworkCore;
using Pvm.Application.Messaging;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Messaging;

public sealed class IntegrationDeliveryRepository(PvmDbContext dbContext)
{
    public async Task<IntegrationMessageDeliveryEntity> RecordReceivedAsync(
        string queueName,
        string messageId,
        string messageType,
        string correlationId,
        string? causationId,
        int deliveryCount,
        DateTimeOffset enqueuedAt,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await dbContext.IntegrationMessageDeliveries
            .SingleOrDefaultAsync(
                delivery => delivery.QueueName == queueName && delivery.MessageId == messageId,
                cancellationToken);

        if (existing is null)
        {
            existing = new IntegrationMessageDeliveryEntity
            {
                Id = Guid.NewGuid(),
                QueueName = queueName,
                MessageId = messageId,
                MessageType = messageType,
                CorrelationId = correlationId,
                CausationId = causationId,
                DeliveryCount = deliveryCount,
                EnqueuedAt = enqueuedAt,
                FirstReceivedAt = now,
                LastReceivedAt = now,
                UpdatedAt = now
            };
            dbContext.IntegrationMessageDeliveries.Add(existing);
        }
        else
        {
            existing.DeliveryCount = Math.Max(existing.DeliveryCount, deliveryCount);
            existing.LastReceivedAt = now;
            existing.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public Task MarkCompletedAsync(Guid id, CancellationToken cancellationToken)
        => SetStatusAsync(id, "Completed", null, null, null, cancellationToken);

    public Task MarkRetryingAsync(Guid id, string code, string summary, CancellationToken cancellationToken)
        => SetStatusAsync(id, "Retrying", code, summary, null, cancellationToken);

    public Task MarkDeadLetteredAsync(
        Guid id,
        string code,
        string summary,
        string reason,
        CancellationToken cancellationToken)
        => SetStatusAsync(id, "DeadLettered", code, summary, reason, cancellationToken);

    private async Task SetStatusAsync(
        Guid id,
        string status,
        string? code,
        string? summary,
        string? deadLetterReason,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var delivery = await dbContext.IntegrationMessageDeliveries
            .SingleAsync(item => item.Id == id, cancellationToken);
        delivery.Status = status;
        delivery.ErrorCode = code;
        delivery.ErrorSummary = OutboxRepository.SafeSummary(summary);
        delivery.DeadLetterReason = deadLetterReason;
        delivery.CompletedAt = status is "Completed" or "DeadLettered" ? now : null;
        delivery.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
