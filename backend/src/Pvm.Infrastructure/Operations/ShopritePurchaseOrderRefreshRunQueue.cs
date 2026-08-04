using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Pvm.Application.Messaging;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Operations;

public sealed record QueuedIntegrationRun(Guid RunId, Guid MessageId, bool Created);

public sealed class ShopritePurchaseOrderRefreshRunQueue(
    PvmDbContext dbContext,
    IIntegrationCommandQueue commandQueue,
    IConfiguration configuration)
{
    public async Task<QueuedIntegrationRun> EnqueueAsync(
        string trigger,
        string initiatedBy,
        string? scheduleKey,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(scheduleKey))
        {
            var existing = await dbContext.IntegrationRuns
                .AsNoTracking()
                .SingleOrDefaultAsync(run => run.ScheduleKey == scheduleKey, cancellationToken);
            if (existing is not null && existing.MessageId is not null)
            {
                return new QueuedIntegrationRun(existing.Id, existing.MessageId.Value, Created: false);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var runId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var correlationId = runId.ToString("D");
        dbContext.IntegrationRuns.Add(new IntegrationRunEntity
        {
            Id = runId,
            RunType = IntegrationRunTypes.ShopritePurchaseOrderRefresh,
            Trigger = trigger,
            InitiatedBy = initiatedBy,
            EnvironmentName = configuration["Pvm:EnvironmentName"] ?? "Development",
            CorrelationId = correlationId,
            MessageId = messageId,
            ScheduleKey = scheduleKey,
            Status = IntegrationRunStatuses.Accepted,
            CreatedAt = now,
            UpdatedAt = now
        });

        try
        {
            await commandQueue.EnqueueAsync(
                IntegrationQueues.ShopritePurchaseOrderRefresh,
                IntegrationMessageTypes.ShopritePurchaseOrderRefreshV1,
                new RefreshShopritePurchaseOrdersMessage(initiatedBy, runId, trigger),
                correlationId,
                messageId: messageId,
                cancellationToken: cancellationToken);
            return new QueuedIntegrationRun(runId, messageId, Created: true);
        }
        catch (DbUpdateException exception) when (
            !string.IsNullOrWhiteSpace(scheduleKey)
            && exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            })
        {
            dbContext.ChangeTracker.Clear();
            var existing = await dbContext.IntegrationRuns
                .AsNoTracking()
                .SingleAsync(run => run.ScheduleKey == scheduleKey, cancellationToken);
            return new QueuedIntegrationRun(
                existing.Id,
                existing.MessageId ?? throw new InvalidOperationException("Scheduled run has no message ID."),
                Created: false);
        }
    }
}
