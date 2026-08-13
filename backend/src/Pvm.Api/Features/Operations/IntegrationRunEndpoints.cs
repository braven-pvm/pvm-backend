using Microsoft.EntityFrameworkCore;
using Pvm.Application.Messaging;
using Pvm.Infrastructure.Acumatica;
using Pvm.Infrastructure.Operations;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Pvm.Infrastructure.Automation;

namespace Pvm.Api.Features.Operations;

public static class IntegrationRunEndpoints
{
    public static IEndpointRouteBuilder MapIntegrationRunEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/integration-runs")
            .RequireAuthorization("Invoices.Read");

        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);

        app.MapGet("/api/operations/summary", GetSummaryAsync)
            .RequireAuthorization("Invoices.Read");

        return app;
    }

    private static async Task<IResult> ListAsync(
        PvmDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var runs = await dbContext.IntegrationRuns
            .AsNoTracking()
            .OrderByDescending(run => run.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        return Results.Ok(runs.Select(ToResponse).ToArray());
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        PvmDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.IntegrationRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        return run is null
            ? Results.NotFound(new { id, message = "Integration run not found." })
            : Results.Ok(ToResponse(run));
    }

    private static async Task<IResult> GetSummaryAsync(
        PvmDbContext dbContext,
        ShopritePurchaseOrderFreshnessService freshnessService,
        AcumaticaInvoiceReconciliationFreshnessService reconciliationFreshnessService,
        AcumaticaPushNotificationHealthService pushNotificationHealthService,
        AutomationPolicyService automationPolicyService,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var freshness = await freshnessService.GetAsync(DateTimeOffset.UtcNow, cancellationToken);
        var reconciliationFreshness = await reconciliationFreshnessService.GetAsync(
            DateTimeOffset.UtcNow,
            cancellationToken);
        var pushNotificationHealth = await pushNotificationHealthService.GetAsync(
            DateTimeOffset.UtcNow,
            cancellationToken);
        var automationPolicy = await automationPolicyService.GetCurrentAsync(cancellationToken);
        var failedSince = DateTimeOffset.UtcNow.AddHours(-24);
        var activeRuns = await dbContext.IntegrationRuns.CountAsync(
            run => run.Status == IntegrationRunStatuses.Accepted
                || run.Status == IntegrationRunStatuses.Running,
            cancellationToken);
        var failedRuns = await dbContext.IntegrationRuns.CountAsync(
            run => run.Status == IntegrationRunStatuses.Failed && run.UpdatedAt >= failedSince,
            cancellationToken);
        var pendingMessages = await dbContext.IntegrationOutboxMessages.CountAsync(
            message => message.Status == "Pending" || message.Status == "Publishing",
            cancellationToken);
        var deadLetters = await dbContext.IntegrationMessageDeliveries.CountAsync(
            delivery => delivery.Status == "DeadLettered",
            cancellationToken);
        var candidateInvoices = await dbContext.InvoiceCandidates.CountAsync(cancellationToken);
        var needsReview = await dbContext.InvoiceCandidates.CountAsync(
            candidate => candidate.Status == "NeedsReview",
            cancellationToken);
        var latestRuns = await dbContext.IntegrationRuns
            .AsNoTracking()
            .OrderByDescending(run => run.CreatedAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        return Results.Ok(new
        {
            environmentName = configuration["Pvm:EnvironmentName"] ?? "Development",
            automationMode = automationPolicy.Mode.ToString(),
            automationEmergencyStop = automationPolicy.EmergencyStop,
            automationPolicyVersion = automationPolicy.Version,
            generatedAt = DateTimeOffset.UtcNow,
            purchaseOrderFreshness = freshness,
            acumaticaReconciliationFreshness = reconciliationFreshness,
            acumaticaPushNotificationHealth = pushNotificationHealth,
            summary = new
            {
                activeRuns,
                failedRuns,
                pendingMessages,
                deadLetters,
                candidateInvoices,
                needsReview
            },
            latestRuns = latestRuns.Select(ToResponse).ToArray()
        });
    }

    private static object ToResponse(IntegrationRunEntity run)
        => new
        {
            run.Id,
            run.RunType,
            run.Trigger,
            run.InitiatedBy,
            run.EnvironmentName,
            run.CorrelationId,
            run.MessageId,
            run.Status,
            run.AttemptCount,
            run.ReceivedCount,
            run.CreatedCount,
            run.UpdatedCount,
            run.UnchangedCount,
            run.SkippedCount,
            run.RevalidatedCount,
            run.FailedCount,
            run.ErrorCode,
            run.ErrorSummary,
            run.CursorBefore,
            run.QueryFrom,
            run.QueryTo,
            run.CursorAfter,
            run.CreatedAt,
            run.UpdatedAt,
            run.StartedAt,
            run.CompletedAt
        };
}
