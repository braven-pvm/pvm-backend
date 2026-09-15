namespace Pvm.Infrastructure.Investec;

/// <summary>
/// Works out which dates one scheduled refresh reads. The window reaches back over earlier runs
/// on purpose, so a missed run is collected by the next one, and it never crosses the date the
/// feed took ownership of the account.
/// </summary>
public static class InvestecRefreshWindow
{
    public static (DateOnly FromDate, DateOnly ToDate) Resolve(
        DateOnly today,
        int lookbackDays,
        DateOnly? feedStartDate)
    {
        var fromDate = today.AddDays(-Math.Max(1, lookbackDays));
        if (feedStartDate is { } start && fromDate < start)
        {
            fromDate = start;
        }

        // A feed start date in the future would invert the window; read that single day instead.
        return fromDate > today ? (fromDate, fromDate) : (fromDate, today);
    }
}
