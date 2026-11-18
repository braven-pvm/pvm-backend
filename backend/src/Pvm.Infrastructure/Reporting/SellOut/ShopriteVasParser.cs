using System.Globalization;
using System.Text.RegularExpressions;
using Pvm.Application.Reporting.SellOut;

namespace Pvm.Infrastructure.Reporting.SellOut;

/// <summary>
/// Reads the Shoprite group vendor portal report "Vendor Article Sales and Stock" (vnd-art-sas).
/// It covers Checkers, Shoprite, Usave and Medirite stores. The parser reads the sheet
/// "Separate View": one row for each store and article, with the units of the last six complete
/// months, the value of the latest month, and the store stock. Refer to
/// docs/design/sell-out-import.md, section 2.2.
/// </summary>
/// <remarks>The report is a sales and stock report. It is not an order, and the import does not read or change Shoprite orders.</remarks>
public sealed partial class ShopriteVasParser : ISellOutParser
{
    public const string SourceName = "shoprite-vas";
    private const string Title = "Vendor Article Sales and Stock";
    private const string DataSheet = "Separate View";
    private const int HeaderSearchRows = 15;

    public string Source => SourceName;

    public bool CanRead(SellOutFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!XlsxReader.LooksLikeXlsx(file.Content))
        {
            return false;
        }

        try
        {
            using var reader = XlsxReader.Open(file.Content);
            return reader.SheetNames.Contains(DataSheet)
                && reader.ReadRows(DataSheet).Take(3).Any(row => row.Cells.Any(cell => cell?.Trim() == Title));
        }
        catch (SellOutFormatException)
        {
            return false;
        }
    }

    public SellOutParseResult Parse(SellOutFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!CanRead(file))
        {
            throw new SellOutFormatException($"The file is not the portal report '{Title}'.");
        }

        using var reader = XlsxReader.Open(file.Content);
        var fileDate = ReadFileDate(reader) ?? file.ReceivedOn;
        var rows = reader.ReadRows(DataSheet).ToList();

        var headerIndex = rows.Take(HeaderSearchRows).ToList()
            .FindIndex(row => row.Cells.Any(cell => cell?.Trim() == "Site") && row.Cells.Any(cell => cell?.Trim() == "Article Key"));
        if (headerIndex < 1)
        {
            throw new SellOutFormatException("The report has no header row with 'Site' and 'Article Key'.");
        }

        // Some columns have their name only on the group row above the header, such as "DC Stock".
        var header = Names(rows[headerIndex].Cells, rows[headerIndex - 1].Cells);
        int Column(string name) => header.IndexOf(name) is var index and >= 0
            ? index
            : throw new SellOutFormatException($"The report has no column '{name}'.");

        var monthColumns = header
            .Select((name, index) => (Match: MonthHeader().Match(name ?? string.Empty), Index: index))
            .Where(column => column.Match.Success)
            .Select(column => (Month: new DateOnly(
                int.Parse(column.Match.Groups[2].Value, CultureInfo.InvariantCulture),
                DateTime.ParseExact(column.Match.Groups[1].Value, "MMM", CultureInfo.InvariantCulture).Month,
                1), column.Index))
            .OrderBy(column => column.Month)
            .ToList();
        if (monthColumns.Count != 6
            || monthColumns.Zip(monthColumns.Skip(1)).Any(pair => pair.Second.Month != pair.First.Month.AddMonths(1)))
        {
            throw new SellOutFormatException("The report must have 6 month columns in a row, such as 'Oct 2026 units'.");
        }

        var (site, articleKey, sellUom, latestValue, stockQty, stockValue, weeks, dcStock) = (
            Column("Site"), Column("Article Key"), Column("Sell UOM"), Column("Latest Period Sales (Excl)"),
            Column("Stock Qty"), Column("Stock Value @MAC"), Column("WOS"), Column("DC Stock"));
        var lastMonth = monthColumns[^1].Month;

        var monthRows = new List<SellOutMonthRow>();
        var stockRows = new List<SellOutStockRow>();
        var issues = new List<SellOutParseIssue>();
        var rowsRead = 0;
        foreach (var (rowNumber, cells) in rows.Skip(headerIndex + 1))
        {
            string Cell(int index) => index < cells.Length ? cells[index]?.Trim() ?? string.Empty : string.Empty;
            if (Cell(site).Length == 0 && Cell(articleKey).Length == 0)
            {
                continue;
            }

            rowsRead++;
            var (storeCode, storeName) = SplitSite(Cell(site));
            var product = Cell(articleKey);
            if (storeCode.Length == 0 || product.Length == 0)
            {
                issues.Add(new SellOutParseIssue(rowNumber, "no-site-or-article", $"{Cell(site)} / {product}"));
                continue;
            }

            try
            {
                var items = ItemsPerSellUnit(Cell(sellUom));
                var units = monthColumns.Select(column => Number(Cell(column.Index)) ?? 0m).ToArray();
                var value = Number(Cell(latestValue));
                var stock = new SellOutStockRow(
                    storeCode, storeName, product, Gtin: null,
                    StockOnHand: Number(Cell(stockQty)) * items,
                    StockOnOrder: null,
                    StockInTransit: null,
                    StockValue: Number(Cell(stockValue)),
                    WeeksOfStock: Number(Cell(weeks)),
                    DcStock: Number(Cell(dcStock)));

                for (var month = 0; month < monthColumns.Count; month++)
                {
                    var monthValue = monthColumns[month].Month == lastMonth ? value : null;
                    if (units[month] == 0m && (monthValue ?? 0m) == 0m)
                    {
                        continue;
                    }

                    monthRows.Add(new SellOutMonthRow(
                        storeCode, storeName, product, Gtin: null, monthColumns[month].Month.Year, monthColumns[month].Month.Month,
                        units[month] * items, monthValue, IsPartial: false));
                }

                stockRows.Add(stock);
            }
            catch (FormatException)
            {
                issues.Add(new SellOutParseIssue(rowNumber, "unreadable-number", $"{storeCode} / {product}"));
            }
        }

        return new SellOutParseResult(
            SourceName, "shoprite-group", fileDate, monthColumns.Select(column => column.Month).ToList(),
            monthRows, stockRows, issues, rowsRead, RowsIgnored: 0);
    }

    private static List<string?> Names(string?[] header, string?[] groupHeader)
    {
        var count = Math.Max(header.Length, groupHeader.Length);
        return Enumerable.Range(0, count)
            .Select(index => (index < header.Length ? header[index]?.Trim() : null) is { Length: > 0 } name
                ? name
                : index < groupHeader.Length ? groupHeader[index]?.Trim() : null)
            .ToList();
    }

    // "0012-EXAMPLE STORE" is the SAP store number 0012 and the store name.
    private static (string Code, string Name) SplitSite(string site)
    {
        var dash = site.IndexOf('-', StringComparison.Ordinal);
        return dash <= 0 ? (string.Empty, site) : (site[..dash].Trim(), site[(dash + 1)..].Trim());
    }

    // "EA-1" is one item. "PK-20" is a pack of 20 items.
    private static decimal ItemsPerSellUnit(string sellUom)
    {
        var match = SellUnit().Match(sellUom);
        return match.Success ? decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 1m;
    }

    private static decimal? Number(string text) =>
        text.Length == 0
            ? null
            : decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static DateOnly? ReadFileDate(XlsxReader reader)
    {
        if (!reader.SheetNames.Contains("Parameter Selection"))
        {
            return null;
        }

        foreach (var (_, cells) in reader.ReadRows("Parameter Selection").Take(20))
        {
            foreach (var cell in cells)
            {
                var match = DateRequested().Match(cell ?? string.Empty);
                if (match.Success)
                {
                    return DateOnly.ParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
            }
        }

        return null;
    }

    [GeneratedRegex(@"^(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec) (\d{4}) units$")]
    private static partial Regex MonthHeader();

    [GeneratedRegex(@"^[A-Z]+-(\d+)$")]
    private static partial Regex SellUnit();

    [GeneratedRegex(@"Date requested:\s*(\d{4}-\d{2}-\d{2})")]
    private static partial Regex DateRequested();
}
