using Pvm.Api.Features.Reports;

namespace Pvm.Api.Tests.Reports;

public sealed class StatementEndpointsTests
{
    [Theory]
    [InlineData("CMR", "2026-10", null, "2026-10-01", "2026-10-01", null)]
    [InlineData("Alchemy", "2026-05", "2026-09", "2026-05-01", "2026-09-01", null)]
    [InlineData(" ", "2026-10", null, null, null, "Choose a company.")]
    [InlineData("CMR", "10-2026", null, null, null, "Give the month as 2026-10.")]
    [InlineData("CMR", "2026-10", "2026-13", null, null, "Give the last month as 2026-10.")]
    [InlineData("CMR", "2026-10", "2026-09", null, null, "The last month is before the first month.")]
    [InlineData("CMR", "2026-01", "2027-01", null, null, "A statement covers at most 12 months.")]
    public void The_period_is_checked(string company, string from, string? to, string? first, string? last, string? problem)
    {
        var (period, message) = StatementEndpoints.ReadPeriod(company, from, to);

        Assert.Equal(problem, message);
        if (problem is null)
        {
            Assert.Equal(DateOnly.Parse(first!, System.Globalization.CultureInfo.InvariantCulture), period.First);
            Assert.Equal(DateOnly.Parse(last!, System.Globalization.CultureInfo.InvariantCulture), period.Last);
        }
    }
}
