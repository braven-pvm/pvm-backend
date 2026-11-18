using System.Globalization;

namespace Pvm.Domain.Reporting;

/// <summary>
/// An Acumatica posting period: period 1 to 12 of a financial year that starts in March.
/// </summary>
/// <remarks>
/// PVM closes a posting period near the 25th of its month, so a posting period is close to a
/// calendar month but not the same. The sell-in load reads by posting period, because the
/// inquiry filters on it on the server. Reports group by <see cref="FinancialMonth"/>.
/// </remarks>
public readonly record struct PostingPeriod
{
    private PostingPeriod(int financialYear, int number)
    {
        FinancialYear = financialYear;
        Number = number;
    }

    public int FinancialYear { get; }

    public int Number { get; }

    /// <summary>The display form that OData filters on, such as "012025" for period 1 of 2025.</summary>
    public string DisplayId => $"{Number:00}{FinancialYear:0000}";

    /// <summary>The internal form that Acumatica stores, such as "202501".</summary>
    public string InternalId => $"{FinancialYear:0000}{Number:00}";

    public static PostingPeriod Create(int financialYear, int number)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, 12);
        ArgumentOutOfRangeException.ThrowIfLessThan(financialYear, 2000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(financialYear, 2999);
        return new PostingPeriod(financialYear, number);
    }

    /// <summary>The posting period with the same number as the financial month of this date.</summary>
    public static PostingPeriod Containing(DateOnly date)
    {
        var month = FinancialMonth.FromDate(date);
        return new PostingPeriod(month.FinancialYear, month.Number);
    }

    /// <summary>Reads the display form "MMYYYY" or the internal form "YYYYMM".</summary>
    public static bool TryParse(string? value, out PostingPeriod period)
    {
        period = default;
        var text = value?.Trim().Replace("-", string.Empty, StringComparison.Ordinal);
        if (text is null || text.Length != 6 || !text.All(char.IsAsciiDigit))
        {
            return false;
        }

        var first = int.Parse(text[..2], CultureInfo.InvariantCulture);
        var (year, number) = first is >= 1 and <= 12
            ? (int.Parse(text[2..], CultureInfo.InvariantCulture), first)
            : (int.Parse(text[..4], CultureInfo.InvariantCulture), int.Parse(text[4..], CultureInfo.InvariantCulture));

        if (number is < 1 or > 12 || year is < 2000 or > 2999)
        {
            return false;
        }

        period = new PostingPeriod(year, number);
        return true;
    }

    public static PostingPeriod Parse(string value) =>
        TryParse(value, out var period)
            ? period
            : throw new FormatException($"'{value}' is not a posting period. Use MMYYYY or YYYYMM.");

    public PostingPeriod AddPeriods(int periods)
    {
        var month = FinancialMonth.Create(FinancialYear, Number).AddMonths(periods);
        return new PostingPeriod(month.FinancialYear, month.Number);
    }

    public int CompareTo(PostingPeriod other) =>
        (FinancialYear * 12 + Number).CompareTo(other.FinancialYear * 12 + other.Number);

    public override string ToString() => DisplayId;
}
