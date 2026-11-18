using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Pvm.Application.Reporting.SellOut;

namespace Pvm.Infrastructure.Reporting.SellOut;

/// <summary>
/// Reads the Makro "Dispo" report: SAP list output as tab-separated text, with the run date on
/// line 1 and the header on line 4. An article has one row for each unit of measure, and each row
/// gives its units, stock and net cost in its own unit. The parser adds the rows of a site and an
/// article up in single items. Refer to docs/design/sell-out-import.md, section 2.1.
/// </summary>
public sealed partial class MakroDispoParser : ISellOutParser
{
    public const string SourceName = "makro-dispo";
    private const int HeaderLine = 4;
    private const int MonthsInFile = 6;

    public string Source => SourceName;

    public bool CanRead(SellOutFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var lines = ReadLines(file, maxLines: HeaderLine);
        if (lines.Length < HeaderLine)
        {
            return false;
        }

        var header = Columns(lines[HeaderLine - 1]);
        return new[] { "Vendor", "Article", "Barcode", "UOM", "Site", "Site Name", "SOH", "Net Cost" }
            .All(name => header.Contains(name));
    }

    public SellOutParseResult Parse(SellOutFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var lines = ReadLines(file, maxLines: int.MaxValue);
        if (!CanRead(file))
        {
            throw new SellOutFormatException("The file does not have the header of the Makro Dispo report.");
        }

        var fileDate = ReadFileDate(lines[0]) ?? file.ReceivedOn;
        var header = lines[HeaderLine - 1].Split('\t').Select(cell => cell.Trim()).ToArray();
        int Column(string name) => Array.IndexOf(header, name) is var index and >= 0
            ? index
            : throw new SellOutFormatException($"The Makro Dispo report has no column '{name}'.");

        var lastMonth = new DateOnly(fileDate.Year, fileDate.Month, 1);
        var months = Enumerable.Range(0, MonthsInFile).Select(offset => lastMonth.AddMonths(offset - MonthsInFile + 1)).ToList();
        var monthColumns = months
            .Select(month => (Month: month, Index: Array.IndexOf(header, $"{month.Month:00}-{month.Year:0000}")))
            .ToList();
        if (monthColumns.Any(column => column.Index < 0))
        {
            throw new SellOutFormatException(
                $"The Makro Dispo report must have the 6 month columns up to {lastMonth.Month:00}-{lastMonth.Year:0000}.");
        }

        var (article, barcode, uom, compo, site, siteName, soh, soo, sit, netCost) = (
            Column("Article"), Column("Barcode"), Column("UOM"), Column("Compo"), Column("Site"), Column("Site Name"),
            Column("SOH"), Column("SOO"), Column("SIT"), Column("Net Cost"));

        var groups = new Dictionary<(string Site, string Article), Group>();
        var issues = new List<SellOutParseIssue>();
        var rowsRead = 0;
        var ignored = 0;
        for (var lineIndex = HeaderLine; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            rowsRead++;
            var cells = line.Split('\t');
            string Cell(int index) => index < cells.Length ? cells[index].Trim() : string.Empty;
            var rowNumber = lineIndex + 1;
            var key = (Site: Cell(site), Article: Cell(article));
            if (key.Site.Length == 0 || key.Article.Length == 0)
            {
                issues.Add(new SellOutParseIssue(rowNumber, "no-site-or-article", $"{key.Site} / {key.Article}"));
                continue;
            }

            // The ** rows have no barcode, cost or stock, and the statements do not count them.
            if (Cell(uom) == "**")
            {
                ignored++;
                continue;
            }

            try
            {
                var items = Number(Cell(compo));
                var cost = Number(Cell(netCost));
                var units = monthColumns.Select(column => Number(Cell(column.Index))).ToArray();
                var stock = (Number(Cell(soh)), Number(Cell(soo)), Number(Cell(sit)));

                if (!groups.TryGetValue(key, out var group))
                {
                    group = new Group(Cell(siteName), units.Length);
                    groups[key] = group;
                }

                for (var month = 0; month < units.Length; month++)
                {
                    group.UnitsEach[month] += units[month] * items;
                    group.Value[month] += units[month] * cost;
                }

                group.OnHand += stock.Item1 * items;
                group.OnOrder += stock.Item2 * items;
                group.InTransit += stock.Item3 * items;
                if (Cell(uom) == "EA" && Cell(barcode).Length > 0)
                {
                    group.Gtin = Cell(barcode);
                }
            }
            catch (FormatException)
            {
                issues.Add(new SellOutParseIssue(rowNumber, "unreadable-number", $"{key.Site} / {key.Article} / {Cell(uom)}"));
            }
        }

        var monthRows = new List<SellOutMonthRow>();
        var stockRows = new List<SellOutStockRow>();
        foreach (var ((storeCode, productCode), group) in groups)
        {
            for (var month = 0; month < months.Count; month++)
            {
                if (group.UnitsEach[month] == 0m && group.Value[month] == 0m)
                {
                    continue;
                }

                monthRows.Add(new SellOutMonthRow(
                    storeCode, group.StoreName, productCode, group.Gtin, months[month].Year, months[month].Month,
                    group.UnitsEach[month], group.Value[month], IsPartial: months[month] == lastMonth));
            }

            stockRows.Add(new SellOutStockRow(
                storeCode, group.StoreName, productCode, group.Gtin, group.OnHand, group.OnOrder, group.InTransit,
                StockValue: null, WeeksOfStock: null, DcStock: null));
        }

        return new SellOutParseResult(SourceName, "makro", fileDate, months, monthRows, stockRows, issues, rowsRead, ignored);
    }

    private static string[] ReadLines(SellOutFile file, int maxLines)
    {
        // SAP writes the list in the Windows code page. Latin-1 reads its letters and digits the same.
        var text = Encoding.Latin1.GetString(file.Content);
        var lines = text.Split('\n', maxLines == int.MaxValue ? int.MaxValue : maxLines + 1);
        return lines.Take(maxLines).Select(line => line.TrimEnd('\r')).ToArray();
    }

    private static HashSet<string> Columns(string line) =>
        line.Split('\t').Select(cell => cell.Trim()).Where(cell => cell.Length > 0).ToHashSet(StringComparer.Ordinal);

    private static DateOnly? ReadFileDate(string line)
    {
        var match = FileDatePattern().Match(line);
        return match.Success
            && DateOnly.TryParseExact(match.Value, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private static decimal Number(string text) =>
        text.Length == 0
            ? 0m
            : decimal.Parse(text.Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\b\d{2}\.\d{2}\.\d{4}\b")]
    private static partial Regex FileDatePattern();

    private sealed class Group(string storeName, int months)
    {
        public string StoreName { get; } = storeName;
        public string? Gtin { get; set; }
        public decimal[] UnitsEach { get; } = new decimal[months];
        public decimal[] Value { get; } = new decimal[months];
        public decimal OnHand { get; set; }
        public decimal OnOrder { get; set; }
        public decimal InTransit { get; set; }
    }
}
