using Pvm.Domain.Reporting;

namespace Pvm.Domain.Tests.Reporting;

public sealed class FinancialMonthTests
{
    [Theory]
    [InlineData(2026, 3, 2027, 1, "Mar")]
    [InlineData(2026, 10, 2027, 8, "Oct")]
    [InlineData(2026, 12, 2027, 10, "Dec")]
    [InlineData(2027, 1, 2027, 11, "Jan")]
    [InlineData(2027, 2, 2027, 12, "Feb")]
    [InlineData(2025, 2, 2025, 12, "Feb")]
    public void Calendar_month_maps_to_financial_year_ending_in_february(
        int year, int month, int financialYear, int number, string label)
    {
        var financialMonth = FinancialMonth.FromCalendar(year, month);

        Assert.Equal(financialYear, financialMonth.FinancialYear);
        Assert.Equal(number, financialMonth.Number);
        Assert.Equal(label, financialMonth.Label);
        Assert.Equal(year, financialMonth.CalendarYear);
        Assert.Equal(month, financialMonth.CalendarMonth);
    }

    [Fact]
    public void Date_uses_its_calendar_month_not_a_posting_period()
    {
        // PVM closes posting periods near the 25th. A document on 27 May is still a May sale.
        var financialMonth = FinancialMonth.FromDate(new DateOnly(2024, 5, 27));

        Assert.Equal(FinancialMonth.Create(2025, 3), financialMonth);
        Assert.Equal(new DateOnly(2024, 5, 1), financialMonth.FirstDay);
    }

    [Theory]
    [InlineData(2027, 1, -1, 2026, 12)]
    [InlineData(2027, 12, 1, 2028, 1)]
    [InlineData(2027, 5, 24, 2029, 5)]
    [InlineData(2027, 5, -17, 2025, 12)]
    [InlineData(2027, 5, 0, 2027, 5)]
    public void Months_can_be_added_across_financial_years(
        int financialYear, int number, int months, int expectedYear, int expectedNumber)
    {
        var result = FinancialMonth.Create(financialYear, number).AddMonths(months);

        Assert.Equal(FinancialMonth.Create(expectedYear, expectedNumber), result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Month_number_outside_1_to_12_is_refused(int number)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FinancialMonth.Create(2027, number));
        Assert.Throws<ArgumentOutOfRangeException>(() => FinancialMonth.FromCalendar(2027, number));
    }

    [Fact]
    public void Text_form_names_year_and_month()
    {
        Assert.Equal("FY2027-08", FinancialMonth.FromCalendar(2026, 10).ToString());
    }
}
