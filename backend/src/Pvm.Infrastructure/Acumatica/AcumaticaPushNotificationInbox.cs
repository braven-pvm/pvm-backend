using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Pvm.Application.Messaging;
using Pvm.Infrastructure.Persistence;

namespace Pvm.Infrastructure.Acumatica;

public sealed class AcumaticaPushNotificationInbox(
    PvmDbContext dbContext,
    IIntegrationCommandQueue commandQueue)
{
    public async Task<AcumaticaPushNotificationReceipt> ReceiveAsync(
        AcumaticaPushNotification notification,
        string rawPayload,
        string sourceEnvironment,
        CancellationToken cancellationToken)
    {
        var receivedAt = DateTimeOffset.UtcNow;
        var eventId = Guid.NewGuid();
        var references = notification.InvoiceReferences();
        var payloadHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(rawPayload))).ToLowerInvariant();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            insert into integration_event_inbox
                ("Id", "SourceEnvironment", "CompanyId", "QueryName", "TransactionId",
                 "NotificationTimestamp", "PayloadJson", "PayloadHash", "InsertedCount",
                 "DeletedCount", "EnqueuedCount", "DuplicateCount", "ReceivedAt", "LastReceivedAt")
            values
                ({eventId}, {sourceEnvironment}, {notification.CompanyId}, {notification.Query}, {notification.Id},
                 {notification.TimeStamp}, {rawPayload}::jsonb, {payloadHash}, {notification.Inserted.Length},
                 {notification.Deleted.Length}, {references.Count}, 0, {receivedAt}, {receivedAt})
            on conflict ("SourceEnvironment", "CompanyId", "QueryName", "TransactionId") do nothing;
            """, cancellationToken);

        if (inserted == 0)
        {
            await dbContext.IntegrationEventInbox
                .Where(item => item.SourceEnvironment == sourceEnvironment
                    && item.CompanyId == notification.CompanyId
                    && item.QueryName == notification.Query
                    && item.TransactionId == notification.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.DuplicateCount, item => item.DuplicateCount + 1)
                    .SetProperty(item => item.LastReceivedAt, receivedAt), cancellationToken);

            var existingId = await dbContext.IntegrationEventInbox
                .Where(item => item.SourceEnvironment == sourceEnvironment
                    && item.CompanyId == notification.CompanyId
                    && item.QueryName == notification.Query
                    && item.TransactionId == notification.Id)
                .Select(item => item.Id)
                .SingleAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AcumaticaPushNotificationReceipt(existingId, true, []);
        }

        var messageIds = new List<Guid>(references.Count);
        foreach (var reference in references)
        {
            var messageId = await commandQueue.EnqueueAsync(
                IntegrationQueues.AcumaticaInvoiceDiscovery,
                IntegrationMessageTypes.AcumaticaInvoiceChangedV1,
                new DiscoverAcumaticaInvoiceMessage(
                    "system:acumatica-push-notification",
                    reference.InvoiceId,
                    notification.Id,
                    notification.TimeStamp,
                    reference.ReferenceNumber),
                notification.Id.ToString("D"),
                eventId.ToString("D"),
                cancellationToken: cancellationToken);
            messageIds.Add(messageId);
        }

        await transaction.CommitAsync(cancellationToken);
        return new AcumaticaPushNotificationReceipt(eventId, false, messageIds);
    }
}

public sealed record AcumaticaPushNotificationReceipt(
    Guid EventId,
    bool Duplicate,
    IReadOnlyList<Guid> MessageIds);
