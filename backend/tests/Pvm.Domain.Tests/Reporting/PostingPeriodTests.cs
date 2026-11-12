using Pvm.Domain.Reporting;

namespace Pvm.Domain.Tests.Reporting;

public sealed class PostingPeriodTests
{
    [Theory]
    [InlineData("012025", 2025, 1)]
    [InlineData("202501", 2025, 1)]
    [InlineData("01-2025", 2025, 1)]
    [InlineData("122026", 2026, 12)]
    [InlineData("202612", 2026, 12)]
    public void Display_and_internal_forms_are_read(string value, int year, int number)
    {
        Assert.True(PostingPeriod.TryParse(value, out var period));
        Assert.Equal(PostingPeriod.Create(year, number), period);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2025")]
    [InlineData("132025")]
    [InlineData("202513")]
    [InlineData("20250A")]
    public void Other_text_is_not_a_period(string? value)
    {
        Assert.False(PostingPeriod.TryParse(value, out _));
        Assert.Throws<FormatException>(() => PostingPeriod.Parse(value ?? string.Empty));
    }

    [Fact]
    public void Both_forms_are_written()
    {
        var period = PostingPeriod.Create(2025, 1);

        Assert.Equal("012025", period.DisplayId);
        Assert.Equal("202501", period.InternalId);
        Assert.Equal("012025", period.ToString());
    }

    [Theory]
    [InlineData(2026, 3, 27, 2027, 1)]
    [InlineData(2027, 2, 10, 2027, 12)]
    [InlineData(2026, 11, 12, 2027, 9)]
    public void Period_of_a_date_has_the_number_of_its_financial_month(
        int year, int month, int day, int financialYear, int number)
    {
        Assert.Equal(PostingPeriod.Create(financialYear, number), PostingPeriod.Containing(new DateOnly(year, month, day)));
    }

    [Fact]
    public void Periods_are_added_across_years_and_compared()
    {
        var period = PostingPeriod.Create(2027, 1);

        Assert.Equal(PostingPeriod.Create(2026, 12), period.AddPeriods(-1));
        Assert.Equal(PostingPeriod.Create(2027, 3), period.AddPeriods(2));
        Assert.True(period.AddPeriods(-1).CompareTo(period) < 0);
        Assert.Equal(0, period.CompareTo(PostingPeriod.Create(2027, 1)));
    }
}
