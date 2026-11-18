using System.Globalization;
using Pvm.Application.Reporting.Statements;

namespace Pvm.Infrastructure.Reporting.Statements;

/// <summary>
/// Lays out a merchandiser statement as an Excel workbook in the layout of the hand-made files:
/// a Summary sheet whose totals and commissions are formulas, one detail sheet for each area, a
/// Makro sheet, and the Dis-Chem sheet when the company has it.
/// </summary>
public static class StatementWorkbook
{
    public static byte[] Write(MerchandiserStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);
        var period = Period(statement.FirstMonth, statement.LastMonth);
        var sheets = new List<XlsxSheet> { Summary(statement, period) };
        sheets.AddRange(statement.Areas.Select(area => Detail(area, period)));
        if (statement.Areas.Any(area => area.MakroStores.Count > 0))
        {
            sheets.Add(Makro(statement, period));
        }

        if (statement.DisChem is { } disChem)
        {
            sheets.Add(DisChemSheet(disChem));
        }

        return XlsxWriter.Write(sheets);
    }

    /// <summary>The file name, such as "CMR Meridian Oct 2026.xlsx".</summary>
    public static string FileName(MerchandiserStatement statement)
    {
        var name = new string(statement.Company.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? ' ' : ch).ToArray()).Trim();
        return $"{name} {Period(statement.FirstMonth, statement.LastMonth).Replace(" to ", " - ", StringComparison.Ordinal)}.xlsx";
    }

    public static string Period(DateOnly first, DateOnly last) =>
        first == last ? Month(first) : $"{Month(first)} to {Month(last)}";

    private static string Month(DateOnly month) => month.ToString("MMM yyyy", CultureInfo.InvariantCulture);

    private static XlsxSheet Summary(MerchandiserStatement statement, string period)
    {
        var rows = new List<IReadOnlyList<XlsxCell?>> { Row(XlsxCell.Of(statement.Title, XlsxStyle.Bold)), Row() };
        var areaTotals = new List<(int Row, decimal Value)>();
        foreach (var area in statement.Areas)
        {
            rows.Add(Row(XlsxCell.Of(area.Name.ToUpperInvariant(), XlsxStyle.Bold), XlsxCell.Of(period, XlsxStyle.Bold)));
            var first = rows.Count + 1;
            rows.AddRange(area.Lines.Select(line => Row(XlsxCell.Of(line.Label), XlsxCell.Of(line.Amount))));
            if (area.MakroStores.Count > 0)
            {
                rows.Add(Row(XlsxCell.Of("Makro"), XlsxCell.Of(area.MakroValue)));
            }

            var last = rows.Count;
            rows.Add(Row(XlsxCell.Of("Total", XlsxStyle.Bold), XlsxCell.Sum($"SUM(B{first}:B{Math.Max(first, last)})", area.Total)));
            var totalRow = rows.Count;
            areaTotals.Add((totalRow, area.Total));
            if (area.Commission is { } commission)
            {
                rows.Add(Row(XlsxCell.Of($"Commission {Rate(area.CommissionRatePercent)}%"), XlsxCell.Sum($"B{totalRow}*{Factor(area.CommissionRatePercent)}", commission, XlsxStyle.Money)));
            }

            rows.Add(Row());
        }

        var grandRow = areaTotals.Count == 1 ? areaTotals[0].Row : 0;
        if (areaTotals.Count > 1)
        {
            rows.Add(Row(XlsxCell.Of("Grand total of all areas", XlsxStyle.Bold), XlsxCell.Sum(string.Join('+', areaTotals.Select(total => $"B{total.Row}")), statement.GrandTotal)));
            grandRow = rows.Count;
        }

        if (statement.Commission is { } companyCommission)
        {
            rows.Add(Row(XlsxCell.Of($"Commission {Rate(statement.RatePercent)}%", XlsxStyle.Bold), XlsxCell.Sum($"B{grandRow}*{Factor(statement.RatePercent)}", companyCommission)));
        }
        else if (statement.RetainerAmount is { } retainer)
        {
            rows.Add(Row(XlsxCell.Of("Retainer", XlsxStyle.Bold), XlsxCell.Of(retainer, XlsxStyle.BoldMoney)));
        }

        return new XlsxSheet("Summary", rows, [42, 18]);
    }

    private static XlsxSheet Detail(StatementArea area, string period)
    {
        var rows = new List<IReadOnlyList<XlsxCell?>>
        {
            Row(XlsxCell.Of(area.Name.ToUpperInvariant(), XlsxStyle.Bold)),
            Row(XlsxCell.Of("Customer", XlsxStyle.Bold), XlsxCell.Of(period, XlsxStyle.Bold)),
        };
        var lineRows = new List<int>();
        foreach (var line in area.Lines)
        {
            rows.Add(Row(XlsxCell.Of(line.Label, XlsxStyle.Bold), XlsxCell.Of(line.Amount, XlsxStyle.BoldMoney)));
            lineRows.Add(rows.Count);
            rows.AddRange(line.Customers.Select(customer => Row(XlsxCell.Of(customer.Name), XlsxCell.Of(customer.Amount))));
        }

        var lineTotal = area.Lines.Sum(line => line.Amount);
        rows.Add(Row(
            XlsxCell.Of("Grand total", XlsxStyle.Bold),
            lineRows.Count == 0 ? XlsxCell.Of(0m, XlsxStyle.BoldMoney) : XlsxCell.Sum(string.Join('+', lineRows.Select(row => $"B{row}")), lineTotal)));
        return new XlsxSheet($"{area.Name} detail", rows, [48, 18]);
    }

    private static XlsxSheet Makro(MerchandiserStatement statement, string period)
    {
        var rows = new List<IReadOnlyList<XlsxCell?>>
        {
            Row(XlsxCell.Of($"MAKRO {statement.Title}", XlsxStyle.Bold), XlsxCell.Of(period, XlsxStyle.Bold)),
            Row(XlsxCell.Of("Store", XlsxStyle.Bold), XlsxCell.Of("Rand value", XlsxStyle.Bold)),
        };
        var first = rows.Count + 1;
        foreach (var area in statement.Areas.Where(area => area.MakroStores.Count > 0))
        {
            rows.AddRange(area.MakroStores.Select(store => Row(XlsxCell.Of(store.Name), XlsxCell.Of(store.Value))));
        }

        var total = statement.Areas.Sum(area => area.MakroValue);
        rows.Add(Row(XlsxCell.Of("Grand total", XlsxStyle.Bold), XlsxCell.Sum($"SUM(B{first}:B{Math.Max(first, rows.Count)})", total)));
        return new XlsxSheet("Makro", rows, [36, 18]);
    }

    private static XlsxSheet DisChemSheet(DisChemYearOnYear disChem)
    {
        var count = disChem.Months.Count;
        var header = new List<XlsxCell?> { XlsxCell.Of("Store", XlsxStyle.Bold) };
        header.AddRange(disChem.Months.Select(month => (XlsxCell?)XlsxCell.Of(Month(month.AddYears(-1)), XlsxStyle.Bold)));
        header.Add(XlsxCell.Of("Total", XlsxStyle.Bold));
        header.AddRange(disChem.Months.Select(month => (XlsxCell?)XlsxCell.Of(Month(month), XlsxStyle.Bold)));
        header.Add(XlsxCell.Of("Total", XlsxStyle.Bold));
        header.Add(XlsxCell.Of("Growth", XlsxStyle.Bold));
        var rows = new List<IReadOnlyList<XlsxCell?>> { Row(XlsxCell.Of("DIS-CHEM: THIS YEAR AND THE YEAR BEFORE", XlsxStyle.Bold)), header };

        // Columns: A store, then last year's months and total, then this year's months and total, then growth.
        var lastTotalColumn = XlsxWriter.Column(count + 1);
        var thisFirstColumn = XlsxWriter.Column(count + 2);
        var thisTotalColumn = XlsxWriter.Column((2 * count) + 2);
        foreach (var store in disChem.Stores)
        {
            var r = rows.Count + 1;
            var row = new List<XlsxCell?> { XlsxCell.Of(store.Name) };
            row.AddRange(store.LastYear.Select(value => (XlsxCell?)XlsxCell.Of(value)));
            row.Add(XlsxCell.Sum($"SUM(B{r}:{XlsxWriter.Column(count)}{r})", store.LastYear.Sum(), XlsxStyle.Money));
            row.AddRange(store.ThisYear.Select(value => (XlsxCell?)XlsxCell.Of(value)));
            row.Add(XlsxCell.Sum($"SUM({thisFirstColumn}{r}:{XlsxWriter.Column((2 * count) + 1)}{r})", store.ThisYear.Sum(), XlsxStyle.Money));
            row.Add(store.GrowthPercent is { } growth
                ? XlsxCell.Sum($"IF({lastTotalColumn}{r}=0,\"\",({thisTotalColumn}{r}-{lastTotalColumn}{r})/{lastTotalColumn}{r})", growth / 100m, XlsxStyle.Percent)
                : null);
            rows.Add(row);
        }

        return new XlsxSheet("Dis-Chem", rows, [40, .. Enumerable.Repeat(14.0, (2 * count) + 3)]);
    }

    private static IReadOnlyList<XlsxCell?> Row(params XlsxCell?[] cells) => cells;

    private static string Rate(decimal? percent) => (percent ?? 0m).ToString("0.##", CultureInfo.InvariantCulture);

    private static string Factor(decimal? percent) => ((percent ?? 0m) / 100m).ToString("0.####", CultureInfo.InvariantCulture);
}
