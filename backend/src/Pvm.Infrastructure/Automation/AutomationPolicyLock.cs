using Microsoft.EntityFrameworkCore;
using Pvm.Infrastructure.Persistence;

namespace Pvm.Infrastructure.Automation;

internal static class AutomationPolicyLock
{
    private const long AdvisoryLockKey = 5788934893304271;

    public static async Task AcquireAsync(PvmDbContext dbContext, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({AdvisoryLockKey})",
            cancellationToken);
    }
}
