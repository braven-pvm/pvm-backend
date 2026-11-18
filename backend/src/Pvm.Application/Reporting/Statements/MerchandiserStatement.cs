namespace Pvm.Application.Reporting.Statements;

// The merchandiser statement: inputs, the result, and the builder that joins them.
// Refer to docs/design/merchandiser-statements.md.

/// <summary>The settings of one company, valid for the statement period.</summary>
public sealed record StatementCompany(
    string Name,
    string Title,
    bool IsRetainer,
    decimal? RatePercent,
    decimal? RetainerAmount,
    bool ShowDisChemYearOnYear,
    IReadOnlyList<StatementAreaSetting> Areas);

public sealed record StatementAreaSetting(
    string Name,
    decimal? CommissionRatePercent,
    IReadOnlyList<StatementLineSetting> Lines,
    IReadOnlyList<Guid> MakroStoreIds);

/// <param name="SalesRepIds">Empty: all reps.</param>
/// <param name="CustomerIds">Empty: all customers of the class and the reps.</param>
public sealed record StatementLineSetting(
    string Label,
    string CustomerClassId,
    IReadOnlyCollection<string> SalesRepIds,
    IReadOnlyCollection<string> CustomerIds,
    bool IsDisChemLine);

/// <summary>The sell-in of one customer in one month (the calendar month of the posting period).</summary>
public sealed record StatementSellIn(
    string CustomerId,
    string CustomerName,
    string? CustomerClassId,
    string? SalesRepId,
    DateOnly Month,
    decimal Amount);

/// <summary>The Makro Rand value of one store in one month.</summary>
public sealed record StatementMakroValue(Guid StoreId, string StoreName, DateOnly Month, decimal Value, bool IsPartial);

public sealed record StatementCustomer(string CustomerId, string Name, decimal Amount);

public sealed record StatementLine(string Label, decimal Amount, IReadOnlyList<StatementCustomer> Customers);

public sealed record StatementMakroStore(string Name, decimal Value);

public sealed record StatementArea(
    string Name,
    IReadOnlyList<StatementLine> Lines,
    decimal MakroValue,
    IReadOnlyList<StatementMakroStore> MakroStores,
    decimal Total,
    decimal? CommissionRatePercent,
    decimal? Commission);

/// <summary>The sell-in of one Dis-Chem store: the same months this year and the year before.</summary>
public sealed record DisChemStoreYears(string Name, IReadOnlyList<decimal> ThisYear, IReadOnlyList<decimal> LastYear, decimal? GrowthPercent);

public sealed record DisChemYearOnYear(IReadOnlyList<DateOnly> Months, IReadOnlyList<DisChemStoreYears> Stores);

public sealed record MerchandiserStatement(
    string Company,
    string Title,
    DateOnly FirstMonth,
    DateOnly LastMonth,
    IReadOnlyList<StatementArea> Areas,
    decimal GrandTotal,
    decimal? RatePercent,
    decimal? Commission,
    decimal? RetainerAmount,
    bool MakroIsPartial,
    DisChemYearOnYear? DisChem);

public static class MerchandiserStatementBuilder
{
    /// <summary>The months of the Dis-Chem sheet: the last 3 months of the period.</summary>
    public const int DisChemMonths = 3;

    /// <summary>
    /// Builds the statement for the months from <paramref name="firstMonth"/> to
    /// <paramref name="lastMonth"/>. The sell-in must hold the months of the period, and for the
    /// Dis-Chem sheet also the same months of the year before.
    /// </summary>
    public static MerchandiserStatement Build(
        StatementCompany company,
        DateOnly firstMonth,
        DateOnly lastMonth,
        IReadOnlyList<StatementSellIn> sellIn,
        IReadOnlyList<StatementMakroValue> makro)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(sellIn);
        ArgumentNullException.ThrowIfNull(makro);
        if (lastMonth < firstMonth)
        {
            throw new ArgumentException("The last month is before the first month.", nameof(lastMonth));
        }

        bool InPeriod(DateOnly month) => month >= firstMonth && month <= lastMonth;
        var periodSellIn = sellIn.Where(row => InPeriod(row.Month)).ToList();
        var periodMakro = makro.Where(value => InPeriod(value.Month)).ToList();

        var areas = company.Areas.Select(area =>
        {
            var lines = area.Lines.Select(line =>
            {
                var customers = periodSellIn
                    .Where(row => Matches(line, row))
                    .GroupBy(row => row.CustomerId)
                    .Select(group => new StatementCustomer(group.Key, DisplayName(group.First().CustomerName), group.Sum(row => row.Amount)))
                    .OrderByDescending(customer => customer.Amount)
                    .ThenBy(customer => customer.Name, StringComparer.Ordinal)
                    .ToList();
                return new StatementLine(line.Label, customers.Sum(customer => customer.Amount), customers);
            }).ToList();

            var stores = periodMakro
                .Where(value => area.MakroStoreIds.Contains(value.StoreId))
                .GroupBy(value => value.StoreId)
                .Select(group => new StatementMakroStore(group.First().StoreName, group.Sum(value => value.Value)))
                .OrderBy(store => store.Name, StringComparer.Ordinal)
                .ToList();
            var makroValue = stores.Sum(store => store.Value);
            var total = lines.Sum(line => line.Amount) + makroValue;
            return new StatementArea(
                area.Name, lines, makroValue, stores, total, area.CommissionRatePercent,
                area.CommissionRatePercent is { } rate ? Percent(total, rate) : null);
        }).ToList();

        var grandTotal = areas.Sum(area => area.Total);
        var disChemLine = company.ShowDisChemYearOnYear
            ? company.Areas.SelectMany(area => area.Lines).FirstOrDefault(line => line.IsDisChemLine)
            : null;

        return new MerchandiserStatement(
            company.Name,
            company.Title,
            firstMonth,
            lastMonth,
            areas,
            grandTotal,
            company.IsRetainer ? null : company.RatePercent,
            company.IsRetainer || company.RatePercent is null ? null : Percent(grandTotal, company.RatePercent.Value),
            company.IsRetainer ? company.RetainerAmount : null,
            periodMakro.Any(value => value.IsPartial && company.Areas.Any(area => area.MakroStoreIds.Contains(value.StoreId))),
            disChemLine is null ? null : DisChem(disChemLine, lastMonth, sellIn));
    }

    /// <summary>The statements show "DIS-CHEM X", where Acumatica has "DIS-CHEM PHARMACIES LTD X".</summary>
    public static string DisplayName(string name) =>
        name.Replace("DIS-CHEM PHARMACIES LTD ", "DIS-CHEM ", StringComparison.OrdinalIgnoreCase).Trim();

    private static bool Matches(StatementLineSetting line, StatementSellIn row) =>
        string.Equals(row.CustomerClassId, line.CustomerClassId, StringComparison.OrdinalIgnoreCase)
        && (line.SalesRepIds.Count == 0 || (row.SalesRepId is not null && line.SalesRepIds.Contains(row.SalesRepId, StringComparer.OrdinalIgnoreCase)))
        && (line.CustomerIds.Count == 0 || line.CustomerIds.Contains(row.CustomerId, StringComparer.OrdinalIgnoreCase));

    private static DisChemYearOnYear DisChem(StatementLineSetting line, DateOnly lastMonth, IReadOnlyList<StatementSellIn> sellIn)
    {
        var months = Enumerable.Range(0, DisChemMonths).Select(offset => lastMonth.AddMonths(offset - DisChemMonths + 1)).ToList();
        var rows = sellIn.Where(row => Matches(line, row)).ToList();
        var stores = rows
            .GroupBy(row => row.CustomerId)
            .Select(group =>
            {
                decimal Sum(DateOnly month) => group.Where(row => row.Month == month).Sum(row => row.Amount);
                var thisYear = months.Select(Sum).ToList();
                var lastYear = months.Select(month => Sum(month.AddYears(-1))).ToList();
                var before = lastYear.Sum();
                return new DisChemStoreYears(
                    DisplayName(group.First().CustomerName),
                    thisYear,
                    lastYear,
                    before == 0m ? null : Math.Round((thisYear.Sum() - before) / before * 100m, 1));
            })
            .Where(store => store.ThisYear.Sum() != 0m || store.LastYear.Sum() != 0m)
            .OrderByDescending(store => store.ThisYear.Sum())
            .ToList();
        return new DisChemYearOnYear(months, stores);
    }

    private static decimal Percent(decimal amount, decimal ratePercent) => Math.Round(amount * ratePercent / 100m, 2);
}
