using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pvm.Application.Messaging;
using Pvm.Infrastructure.Persistence;

namespace Pvm.Infrastructure.Operations;

public sealed record ShopritePurchaseOrderFreshness(
    string Status,
    DateTimeOffset? LastSuccessfulRefreshAt,
    double? AgeMinutes,
    int StaleAfterMinutes,
    bool AllowsAutomaticProcessing);

public sealed class ShopritePurchaseOrderFreshnessService(
    PvmDbContext dbContext,
    IOptions<ShopritePurchaseOrderRefreshOptions> options)
{
    public async Task<ShopritePurchaseOrderFreshness> GetAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var lastSuccess = await dbContext.IntegrationRuns
            .AsNoTracking()
            .Where(run =>
                run.RunType == IntegrationRunTypes.ShopritePurchaseOrderRefresh
                && run.Status == IntegrationRunStatuses.Succeeded
                && run.CompletedAt != null)
            .OrderByDescending(run => run.CompletedAt)
            .Select(run => run.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var threshold = options.Value.StaleAfterMinutes;
        if (lastSuccess is null)
        {
            return new ShopritePurchaseOrderFreshness(
                "Unknown", null, null, threshold, AllowsAutomaticProcessing: false);
        }

        var age = Math.Max(0, (now - lastSuccess.Value).TotalMinutes);
        var isStale = age > threshold;
        return new ShopritePurchaseOrderFreshness(
            isStale ? "Stale" : "Healthy",
            lastSuccess,
            age,
            threshold,
            AllowsAutomaticProcessing: !isStale);
    }
}
