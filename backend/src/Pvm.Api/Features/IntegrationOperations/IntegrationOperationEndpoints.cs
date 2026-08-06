using Microsoft.EntityFrameworkCore;
using Pvm.Api.Auth;
using Pvm.Application.Messaging;
using Pvm.Infrastructure.Operations;
using Pvm.Infrastructure.Persistence;

namespace Pvm.Api.Features.IntegrationOperations;

public static class IntegrationOperationEndpoints
{
    public static IEndpointRouteBuilder MapIntegrationOperationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/integration-messages")
            .RequireAuthorization("Admin");

        group.MapGet("/", ListAsync);
        group.MapPost("/acumatica-discovery", EnqueueAcumaticaDiscoveryAsync);
        group.MapPost("/shoprite-po-refresh", EnqueueShopriteRefreshAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(PvmDbContext dbContext, CancellationToken cancellationToken)
    {
        var outboxTotal = await dbContext.IntegrationOutboxMessages.CountAsync(cancellationToken);
        var deliveryTotal = await dbContext.IntegrationMessageDeliveries.CountAsync(cancellationToken);
        var pending = await dbContext.IntegrationOutboxMessages.CountAsync(
            message => message.Status == "Pending" || message.Status == "Publishing",
            cancellationToken);
        var published = await dbContext.IntegrationOutboxMessages.CountAsync(
            message => message.Status == "Published",
            cancellationToken);
        var retrying = await dbContext.IntegrationMessageDeliveries.CountAsync(
            delivery => delivery.Status == "Retrying",
            cancellationToken);
        var deadLettered = await dbContext.IntegrationMessageDeliveries.CountAsync(
            delivery => delivery.Status == "DeadLettered",
            cancellationToken);
        var outbox = await dbContext.IntegrationOutboxMessages
            .AsNoTracking()
            .OrderByDescending(message => message.CreatedAt)
            .Take(100)
            .Select(message => new
            {
                message.Id,
                message.QueueName,
                message.MessageType,
                message.CorrelationId,
                message.Status,
                message.PublishAttempts,
                message.LastErrorCode,
                message.LastErrorSummary,
                message.CreatedAt,
                message.UpdatedAt,
                message.PublishedAt
            })
            .ToListAsync(cancellationToken);
        var deliveries = await dbContext.IntegrationMessageDeliveries
            .AsNoTracking()
            .OrderByDescending(delivery => delivery.UpdatedAt)
            .Take(100)
            .Select(delivery => new
            {
                delivery.Id,
                delivery.QueueName,
                delivery.MessageId,
                delivery.MessageType,
                delivery.CorrelationId,
                delivery.Status,
                delivery.DeliveryCount,
                delivery.ErrorCode,
                delivery.ErrorSummary,
                delivery.DeadLetterReason,
                delivery.EnqueuedAt,
                delivery.UpdatedAt,
                delivery.CompletedAt
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(new
        {
            summary = new
            {
                outboxTotal,
                deliveryTotal,
                pending,
                published,
                retrying,
                deadLettered
            },
            outbox,
            deliveries
        });
    }

    private static async Task<IResult> EnqueueAcumaticaDiscoveryAsync(
        AcumaticaInvoiceReconciliationRunQueue runQueue,
        CurrentAppUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var queued = await runQueue.EnqueueAsync(
            IntegrationRunTriggers.Manual,
            CurrentUser(currentUser),
            scheduleKey: null,
            DateTimeOffset.UtcNow,
            lookbackDays: null,
            cancellationToken);
        return Results.Accepted(
            $"/api/integration-runs/{queued.RunId:D}",
            new { queued.RunId, queued.MessageId, queued.Created });
    }

    private static async Task<IResult> EnqueueShopriteRefreshAsync(
        ShopritePurchaseOrderRefreshRunQueue runQueue,
        CurrentAppUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var queued = await runQueue.EnqueueAsync(
            IntegrationRunTriggers.Manual,
            CurrentUser(currentUser),
            scheduleKey: null,
            cancellationToken);
        return Results.Accepted(
            $"/api/integration-runs/{queued.RunId:D}",
            new { queued.RunId, queued.MessageId, queued.Created });
    }

    private static string CurrentUser(CurrentAppUserAccessor currentUser)
        => currentUser.User?.Email ?? "unknown";
}
