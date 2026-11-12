namespace Pvm.Domain.Reporting;

/// <summary>
/// A calendar month placed in PVM's financial year, which runs from March to February and
/// is named after the calendar year in which it ends: March 2026 is month 1 of 2027.
/// </summary>
/// <remarks>
/// Reports group by the calendar month of the document date, as the sales workbook does.
/// They do not use the Acumatica posting period, because PVM closes a posting period near
/// the 25th, so a posting period is not a calendar month.
/// </remarks>
public readonly record struct FinancialMonth
{
    /// <summary>The calendar month in which a financial year starts.</summary>
    public const int FirstCalendarMonth = 3;

    private static readonly string[] Labels =
        ["Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec", "Jan", "Feb"];

    private FinancialMonth(int financialYear, int number)
    {
        FinancialYear = financialYear;
        Number = number;
    }

    /// <summary>The financial year, named after the calendar year in which it ends.</summary>
    public int FinancialYear { get; }

    /// <summary>The month in the financial year, from 1 (March) to 12 (February).</summary>
    public int Number { get; }

    public int CalendarYear => Number <= 10 ? FinancialYear - 1 : FinancialYear;

    public int CalendarMonth => (Number + FirstCalendarMonth - 2) % 12 + 1;

    public DateOnly FirstDay => new(CalendarYear, CalendarMonth, 1);

    /// <summary>The short month name used in report column headers, such as "Mar".</summary>
    public string Label => Labels[Number - 1];

    public static FinancialMonth FromDate(DateOnly date) => FromCalendar(date.Year, date.Month);

    public static FinancialMonth FromCalendar(int year, int month)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);

        var financialYear = month >= FirstCalendarMonth ? year + 1 : year;
        var number = (month - FirstCalendarMonth + 12) % 12 + 1;
        return new FinancialMonth(financialYear, number);
    }

    public static FinancialMonth Create(int financialYear, int number)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, 12);
        return new FinancialMonth(financialYear, number);
    }

    public FinancialMonth AddMonths(int months)
    {
        var index = FinancialYear * 12 + (Number - 1) + months;
        var year = (int)Math.Floor(index / 12.0);
        return new FinancialMonth(year, index - year * 12 + 1);
    }

    public override string ToString() => $"FY{FinancialYear}-{Number:00}";
}
