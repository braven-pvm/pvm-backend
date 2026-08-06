using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pvm.Application.Messaging;
using Pvm.Infrastructure.Acumatica;
using Pvm.Infrastructure.Persistence;

namespace Pvm.Infrastructure.Operations;

public sealed record AcumaticaInvoiceReconciliationFreshness(
    string Status,
    DateTimeOffset? LastSuccessfulReconciliationAt,
    DateTimeOffset? CursorAfter,
    double? AgeMinutes,
    int StaleAfterMinutes);

public sealed class AcumaticaInvoiceReconciliationFreshnessService(
    PvmDbContext dbContext,
    IOptions<AcumaticaReconciliationOptions> options)
{
    public async Task<AcumaticaInvoiceReconciliationFreshness> GetAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var latest = await dbContext.IntegrationRuns
            .AsNoTracking()
            .Where(run => run.RunType == IntegrationRunTypes.AcumaticaInvoiceReconciliation
                && run.Status == IntegrationRunStatuses.Succeeded
                && run.CompletedAt != null)
            .OrderByDescending(run => run.CompletedAt)
            .Select(run => new { run.CompletedAt, run.CursorAfter })
            .FirstOrDefaultAsync(cancellationToken);
        var threshold = Math.Max(1, options.Value.StaleAfterMinutes);

        if (latest?.CompletedAt is null)
        {
            return new AcumaticaInvoiceReconciliationFreshness(
                "Unknown", null, null, null, threshold);
        }

        var age = now - latest.CompletedAt.Value;
        return new AcumaticaInvoiceReconciliationFreshness(
            age > TimeSpan.FromMinutes(threshold) ? "Stale" : "Healthy",
            latest.CompletedAt,
            latest.CursorAfter,
            Math.Max(0, age.TotalMinutes),
            threshold);
    }
}
