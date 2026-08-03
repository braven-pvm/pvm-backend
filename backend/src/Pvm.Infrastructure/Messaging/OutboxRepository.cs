using Microsoft.EntityFrameworkCore;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Messaging;

public sealed record OutboxLease(Guid LeaseId, IReadOnlyList<IntegrationOutboxMessageEntity> Messages);

public sealed class OutboxRepository(PvmDbContext dbContext)
{
    public async Task<OutboxLease> ClaimAsync(
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var leaseId = Guid.NewGuid();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var messages = await dbContext.IntegrationOutboxMessages
            .FromSqlInterpolated($$"""
                SELECT * FROM integration_outbox_messages
                WHERE ("Status" = 'Pending' AND "AvailableAt" <= {{now}})
                   OR ("Status" = 'Publishing' AND "LeaseExpiresAt" < {{now}})
                ORDER BY "CreatedAt"
                LIMIT {{batchSize}}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            message.Status = "Publishing";
            message.LeaseId = leaseId;
            message.LeaseExpiresAt = now.Add(leaseDuration);
            message.PublishAttempts++;
            message.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new OutboxLease(leaseId, messages);
    }

    public Task MarkPublishedAsync(Guid messageId, Guid leaseId, CancellationToken cancellationToken)
        => UpdateLeaseAsync(messageId, leaseId, "Published", null, null, null, cancellationToken);

    public Task ReleaseAsync(
        Guid messageId,
        Guid leaseId,
        string errorCode,
        string errorSummary,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
        => UpdateLeaseAsync(messageId, leaseId, "Pending", errorCode, errorSummary, retryDelay, cancellationToken);

    private async Task UpdateLeaseAsync(
        Guid messageId,
        Guid leaseId,
        string status,
        string? errorCode,
        string? errorSummary,
        TimeSpan? retryDelay,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var updated = await dbContext.IntegrationOutboxMessages
            .Where(message => message.Id == messageId && message.LeaseId == leaseId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.Status, status)
                .SetProperty(message => message.LeaseId, (Guid?)null)
                .SetProperty(message => message.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(message => message.LastErrorCode, errorCode)
                .SetProperty(message => message.LastErrorSummary, SafeSummary(errorSummary))
                .SetProperty(message => message.AvailableAt, retryDelay.HasValue ? now.Add(retryDelay.Value) : now)
                .SetProperty(message => message.PublishedAt, status == "Published" ? now : (DateTimeOffset?)null)
                .SetProperty(message => message.UpdatedAt, now), cancellationToken);

        if (updated != 1)
        {
            throw new InvalidOperationException($"Outbox lease {leaseId} no longer owns message {messageId}.");
        }
    }

    public static string? SafeSummary(string? value)
        => DiagnosticSummary.Safe(value);
}
