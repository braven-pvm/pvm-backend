using Pvm.Infrastructure.Investec;
using Xunit;

namespace Pvm.Infrastructure.Tests.Investec;

public sealed class InvestecRefreshWindowTests
{
    private static readonly DateOnly Today = new(2026, 9, 15);

    [Fact]
    public void Resolve_reaches_back_by_the_lookback_when_no_start_date_is_set()
    {
        var (from, to) = InvestecRefreshWindow.Resolve(Today, lookbackDays: 7, feedStartDate: null);

        Assert.Equal(new DateOnly(2026, 9, 8), from);
        Assert.Equal(Today, to);
    }

    [Fact]
    public void Resolve_never_reaches_before_the_feed_start_date()
    {
        // The manual import owns everything before 15 September and numbers it differently,
        // so a 7-day lookback must not pull 8 to 14 September back in.
        var (from, to) = InvestecRefreshWindow.Resolve(
            Today,
            lookbackDays: 7,
            feedStartDate: new DateOnly(2026, 9, 15));

        Assert.Equal(new DateOnly(2026, 9, 15), from);
        Assert.Equal(Today, to);
    }

    [Fact]
    public void Resolve_keeps_the_lookback_once_it_is_clear_of_the_start_date()
    {
        var (from, to) = InvestecRefreshWindow.Resolve(
            new DateOnly(2026, 10, 1),
            lookbackDays: 7,
            feedStartDate: new DateOnly(2026, 9, 15));

        Assert.Equal(new DateOnly(2026, 9, 24), from);
        Assert.Equal(new DateOnly(2026, 10, 1), to);
    }

    [Fact]
    public void Resolve_treats_a_future_start_date_as_a_single_day()
    {
        var (from, to) = InvestecRefreshWindow.Resolve(
            Today,
            lookbackDays: 7,
            feedStartDate: new DateOnly(2026, 9, 20));

        Assert.Equal(new DateOnly(2026, 9, 20), from);
        Assert.Equal(new DateOnly(2026, 9, 20), to);
        Assert.True(from <= to);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Resolve_treats_a_nonsense_lookback_as_one_day(int lookbackDays)
    {
        var (from, to) = InvestecRefreshWindow.Resolve(Today, lookbackDays, feedStartDate: null);

        Assert.Equal(new DateOnly(2026, 9, 14), from);
        Assert.Equal(Today, to);
    }
}
