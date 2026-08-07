using Microsoft.EntityFrameworkCore;
using Pvm.Infrastructure.Acumatica;
using Pvm.Infrastructure.Persistence;

namespace Pvm.Api.Features.AcumaticaPushNotifications;

public static class AcumaticaPushNotificationAdminEndpoints
{
    public static IEndpointRouteBuilder MapAcumaticaPushNotificationAdminEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/acumatica-events", ListAsync)
            .RequireAuthorization("Admin");
        return app;
    }

    private static async Task<IResult> ListAsync(
        PvmDbContext dbContext,
        AcumaticaPushNotificationHealthService healthService,
        CancellationToken cancellationToken)
    {
        var health = await healthService.GetAsync(DateTimeOffset.UtcNow, cancellationToken);
        var events = await dbContext.IntegrationEventInbox
            .AsNoTracking()
            .OrderByDescending(item => item.LastReceivedAt)
            .Take(100)
            .Select(item => new
            {
                item.Id,
                item.SourceEnvironment,
                item.CompanyId,
                item.QueryName,
                item.TransactionId,
                item.NotificationTimestamp,
                item.PayloadHash,
                item.InsertedCount,
                item.DeletedCount,
                item.EnqueuedCount,
                item.DuplicateCount,
                item.ReceivedAt,
                item.LastReceivedAt
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(new { health, events });
    }
}
