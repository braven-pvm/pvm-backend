using Microsoft.EntityFrameworkCore;
using Pvm.Api.Auth;
using Pvm.Application.Messaging;
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

    private static Task<IResult> EnqueueAcumaticaDiscoveryAsync(
        IIntegrationCommandQueue queue,
        CurrentAppUserAccessor currentUser,
        CancellationToken cancellationToken)
        => EnqueueAsync(
            queue,
            IntegrationQueues.AcumaticaInvoiceDiscovery,
            IntegrationMessageTypes.AcumaticaInvoiceDiscoveryV1,
            new DiscoverAcumaticaInvoicesMessage(CurrentUser(currentUser)),
            cancellationToken);

    private static Task<IResult> EnqueueShopriteRefreshAsync(
        IIntegrationCommandQueue queue,
        CurrentAppUserAccessor currentUser,
        CancellationToken cancellationToken)
        => EnqueueAsync(
            queue,
            IntegrationQueues.ShopritePurchaseOrderRefresh,
            IntegrationMessageTypes.ShopritePurchaseOrderRefreshV1,
            new RefreshShopritePurchaseOrdersMessage(CurrentUser(currentUser)),
            cancellationToken);

    private static async Task<IResult> EnqueueAsync<T>(
        IIntegrationCommandQueue queue,
        string queueName,
        string messageType,
        T data,
        CancellationToken cancellationToken)
    {
        var messageId = Guid.NewGuid();
        await queue.EnqueueAsync(
            queueName,
            messageType,
            data,
            messageId.ToString("D"),
            messageId: messageId,
            cancellationToken: cancellationToken);
        return Results.Accepted($"/api/admin/integration-messages", new { messageId });
    }

    private static string CurrentUser(CurrentAppUserAccessor currentUser)
        => currentUser.User?.Email ?? "unknown";
}
