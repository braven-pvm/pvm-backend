using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pvm.Infrastructure.Persistence;

namespace Pvm.Infrastructure.Acumatica;

public sealed class AcumaticaPushNotificationHealthService(
    PvmDbContext dbContext,
    IOptions<AcumaticaPushNotificationOptions> configuredOptions)
{
    public async Task<AcumaticaPushNotificationHealth> GetAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var options = configuredOptions.Value;
        var configured = options.Secret?.Length >= 32
            && options.AllowedCompanies.Length > 0
            && options.AllowedQueries.Length > 0;
        var eventCount = await dbContext.IntegrationEventInbox.CountAsync(cancellationToken);
        var duplicateCount = await dbContext.IntegrationEventInbox.SumAsync(
            item => (long)item.DuplicateCount,
            cancellationToken);
        var latest = await dbContext.IntegrationEventInbox
            .AsNoTracking()
            .OrderByDescending(item => item.LastReceivedAt)
            .Select(item => new
            {
                item.LastReceivedAt,
                item.NotificationTimestamp
            })
            .FirstOrDefaultAsync(cancellationToken);
        var sourceOccurredAt = latest is null
            ? null
            : NotificationTime(latest.NotificationTimestamp, latest.LastReceivedAt);
        var lagSeconds = sourceOccurredAt is null || latest is null
            ? (double?)null
            : Math.Max(0, (latest.LastReceivedAt - sourceOccurredAt.Value).TotalSeconds);

        return new AcumaticaPushNotificationHealth(
            configured ? latest is null ? "Waiting" : "Healthy" : "NotConfigured",
            configured,
            latest?.LastReceivedAt,
            latest is null ? null : Math.Max(0, (now - latest.LastReceivedAt).TotalMinutes),
            sourceOccurredAt,
            lagSeconds,
            eventCount,
            duplicateCount);
    }

    private static DateTimeOffset? NotificationTime(long ticks, DateTimeOffset receivedAt)
    {
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
        {
            return null;
        }

        var value = new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc));
        return value <= receivedAt.AddMinutes(5) ? value : null;
    }
}

public sealed record AcumaticaPushNotificationHealth(
    string Status,
    bool Configured,
    DateTimeOffset? LastReceivedAt,
    double? AgeMinutes,
    DateTimeOffset? SourceOccurredAt,
    double? LastEventLagSeconds,
    int EventCount,
    long DuplicateCount);
