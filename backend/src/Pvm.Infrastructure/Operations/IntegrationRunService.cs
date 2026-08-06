using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Pvm.Application.Messaging;
using Pvm.Infrastructure.Acumatica;
using Pvm.Infrastructure.Messaging;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Pvm.Infrastructure.Shoprite;

namespace Pvm.Infrastructure.Operations;

public sealed class IntegrationRunService(
    PvmDbContext dbContext,
    IConfiguration configuration)
{
    public async Task<IntegrationRunEntity> StartShopritePoRefreshAsync(
        Guid runId,
        Guid messageId,
        string correlationId,
        RefreshShopritePurchaseOrdersMessage command,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var run = await dbContext.IntegrationRuns
            .SingleOrDefaultAsync(item => item.Id == runId, cancellationToken);
        if (run is null)
        {
            run = new IntegrationRunEntity
            {
                Id = runId,
                RunType = IntegrationRunTypes.ShopritePurchaseOrderRefresh,
                Trigger = command.Trigger,
                InitiatedBy = command.RequestedBy,
                EnvironmentName = configuration["Pvm:EnvironmentName"] ?? "Development",
                CorrelationId = correlationId,
                MessageId = messageId,
                Status = IntegrationRunStatuses.Accepted,
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.IntegrationRuns.Add(run);
        }

        run.Status = IntegrationRunStatuses.Running;
        run.AttemptCount++;
        run.StartedAt ??= now;
        run.CompletedAt = null;
        run.ErrorCode = null;
        run.ErrorSummary = null;
        run.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return run;
    }

    public Task<DateTimeOffset?> GetLatestSuccessfulAcumaticaCursorAsync(
        DateTimeOffset notAfter,
        CancellationToken cancellationToken)
        => dbContext.IntegrationRuns
            .Where(run => run.RunType == IntegrationRunTypes.AcumaticaInvoiceReconciliation
                && run.Status == IntegrationRunStatuses.Succeeded
                && run.CursorAfter != null
                && run.CursorAfter <= notAfter)
            .MaxAsync(run => run.CursorAfter, cancellationToken);

    public async Task StartAcumaticaInvoiceReconciliationAsync(
        Guid runId,
        Guid messageId,
        string correlationId,
        ReconcileAcumaticaInvoicesMessage command,
        DateTimeOffset? cursorBefore,
        DateTimeOffset queryFrom,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.IntegrationRuns.SingleAsync(item => item.Id == runId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        run.MessageId = messageId;
        run.CorrelationId = correlationId;
        run.Trigger = command.Trigger;
        run.InitiatedBy = command.RequestedBy;
        run.Status = IntegrationRunStatuses.Running;
        run.AttemptCount++;
        run.CursorBefore = cursorBefore;
        run.QueryFrom = queryFrom;
        run.QueryTo = command.QueryTo;
        run.CursorAfter = null;
        run.StartedAt ??= now;
        run.CompletedAt = null;
        run.ErrorCode = null;
        run.ErrorSummary = null;
        run.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAcumaticaInvoiceReconciliationAsync(
        Guid runId,
        DateTimeOffset cursorAfter,
        AcumaticaInvoiceRefreshResult result,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.IntegrationRuns.SingleAsync(item => item.Id == runId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        run.Status = IntegrationRunStatuses.Succeeded;
        run.ReceivedCount = result.Received;
        run.CreatedCount = result.Created;
        run.UpdatedCount = result.Updated;
        run.UnchangedCount = result.Unchanged;
        run.FailedCount = 0;
        run.CursorAfter = cursorAfter;
        run.CompletedAt = now;
        run.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteShopritePoRefreshAsync(
        Guid runId,
        ShopritePurchaseOrderRefreshResult result,
        int revalidatedCount,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.IntegrationRuns.SingleAsync(item => item.Id == runId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        run.Status = IntegrationRunStatuses.Succeeded;
        run.ReceivedCount = result.Received;
        run.CreatedCount = result.Created;
        run.UpdatedCount = result.Updated;
        run.UnchangedCount = result.Unchanged;
        run.SkippedCount = result.Skipped;
        run.RevalidatedCount = revalidatedCount;
        run.FailedCount = 0;
        run.CompletedAt = now;
        run.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(
        Guid runId,
        string errorCode,
        string? errorSummary,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.IntegrationRuns.SingleAsync(item => item.Id == runId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        run.Status = IntegrationRunStatuses.Failed;
        run.FailedCount = 1;
        run.ErrorCode = errorCode;
        run.ErrorSummary = OutboxRepository.SafeSummary(errorSummary);
        run.CompletedAt = now;
        run.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
