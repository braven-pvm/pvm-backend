using System.Globalization;
using System.Text.RegularExpressions;
using Pvm.Application.Reporting.SellOut;

namespace Pvm.Infrastructure.Reporting.SellOut;

/// <summary>
/// Reads the invoice list of a pharmacy wholesaler: one line for each invoice line to a pharmacy.
/// Each wholesaler has its own columns, so one instance serves one wholesaler. The parser adds the
/// lines up to pharmacy, product and month. The pharmacy is the "store". Refer to
/// docs/design/sell-out-import.md, section 2.3.
/// </summary>
public sealed partial class WholesalerListParser : ISellOutParser
{
    private const int HeaderSearchRows = 5;

    private readonly Layout _layout;

    private WholesalerListParser(Layout layout) => _layout = layout;

    public string Source => _layout.Source;

    public static WholesalerListParser TopMed() => new(new Layout(
        "wholesaler-topmed", Date: "DATE", StoreCode: "ACCOUNT", StoreName: "NAME", Product: "PRODUCT", Gtin: null,
        Quantity: "QTY", Value: "SALES TOTAL", Marker: "TRANS NO"));

    public static WholesalerListParser Cj() => new(new Layout(
        "wholesaler-cj", Date: "DATE", StoreCode: "ACCOUNT", StoreName: "NAME", Product: "CODE", Gtin: null,
        Quantity: "PQTY", Value: "VALUE", Marker: "SUPPACNT"));

    public static WholesalerListParser AlphaPharm() => new(new Layout(
        "wholesaler-alphapharm", Date: "INV_DATE", StoreCode: "PARTY_NUMBER", StoreName: "PARTY_NAME", Product: "ITEM_NUMBER",
        Gtin: "BAR_CODE", Quantity: "QUANTITY_INVOICED", Value: "EXTENDED_AMOUNT", Marker: "NAPPI_CODE"));

    // Arrie Nel gives no account number, so the customer name is the store code.
    public static WholesalerListParser ArrieNel() => new(new Layout(
        "wholesaler-arrienel", Date: "DATE", StoreCode: "CUSTOMER NAME", StoreName: "CUSTOMER NAME", Product: "PRODUCT", Gtin: null,
        Quantity: "QTY", Value: "SALES TOTAL", Marker: "INV NO."));

    public static IReadOnlyList<WholesalerListParser> All() => [TopMed(), Cj(), AlphaPharm(), ArrieNel()];

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
            return FindSheet(reader) is not null;
        }
        catch (SellOutFormatException)
        {
            return false;
        }
    }

    public SellOutParseResult Parse(SellOutFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        using var reader = XlsxReader.Open(file.Content);
        var (sheet, headerRow) = FindSheet(reader)
            ?? throw new SellOutFormatException($"The file has no sheet with the columns of {_layout.Source}.");

        var rows = reader.ReadRows(sheet).ToList();
        var header = rows.Single(row => row.RowNumber == headerRow).Cells.Select(Normalize).ToList();
        int Column(string? name) => name is null ? -1 : header.IndexOf(name);
        var (date, storeCode, storeName, product, gtin, quantity, value) = (
            Column(_layout.Date), Column(_layout.StoreCode), Column(_layout.StoreName), Column(_layout.Product),
            Column(_layout.Gtin), Column(_layout.Quantity), Column(_layout.Value));

        var groups = new Dictionary<(string Store, string Product, DateOnly Month), Group>();
        var issues = new List<SellOutParseIssue>();
        var rowsRead = 0;
        var ignored = 0;
        foreach (var (rowNumber, cells) in rows.Where(row => row.RowNumber > headerRow))
        {
            string Cell(int index) => index >= 0 && index < cells.Length ? Code(cells[index]) : string.Empty;
            if (cells.All(cell => string.IsNullOrWhiteSpace(cell)))
            {
                continue;
            }

            // The total row at the end has numbers but no customer and no product.
            if (Cell(storeCode).Length == 0 && Cell(product).Length == 0)
            {
                ignored++;
                continue;
            }

            rowsRead++;
            var key = $"{Cell(storeCode)} / {Cell(product)}";
            if (Cell(storeCode).Length == 0 || Cell(product).Length == 0)
            {
                issues.Add(new SellOutParseIssue(rowNumber, "no-customer-or-product", key));
                continue;
            }

            try
            {
                var day = ReadDate(Cell(date));
                var units = Number(Cell(quantity));
                var amount = Number(Cell(value));
                var month = new DateOnly(day.Year, day.Month, 1);
                if (!groups.TryGetValue((Cell(storeCode), Cell(product), month), out var group))
                {
                    group = new Group(Cell(storeName).Length > 0 ? Cell(storeName) : Cell(storeCode));
                    groups[(Cell(storeCode), Cell(product), month)] = group;
                }

                group.Units += units;
                group.Value += amount;
                if (gtin >= 0 && Cell(gtin).Length > 0)
                {
                    group.Gtin = Cell(gtin);
                }
            }
            catch (FormatException)
            {
                issues.Add(new SellOutParseIssue(rowNumber, "unreadable-date-or-number", key));
            }
        }

        var monthRows = groups
            .Select(pair => new SellOutMonthRow(
                pair.Key.Store, pair.Value.StoreName, pair.Key.Product, pair.Value.Gtin, pair.Key.Month.Year, pair.Key.Month.Month,
                pair.Value.Units, pair.Value.Value, IsPartial: false))
            .ToList();
        var months = groups.Keys.Select(key => key.Month).Distinct().Order().ToList();
        // A wholesaler list has no date of its own.
        return new SellOutParseResult(_layout.Source, _layout.Source, file.ReceivedOn, months, monthRows, [], issues, rowsRead, ignored);
    }

    private (string Sheet, int HeaderRow)? FindSheet(XlsxReader reader)
    {
        string?[] required = [_layout.Date, _layout.StoreCode, _layout.StoreName, _layout.Product, _layout.Gtin, _layout.Quantity, _layout.Value, _layout.Marker];
        foreach (var sheet in reader.SheetNames)
        {
            foreach (var (rowNumber, cells) in reader.ReadRows(sheet).Take(HeaderSearchRows))
            {
                var names = cells.Select(Normalize).Where(name => name is not null).ToHashSet(StringComparer.Ordinal);
                if (required.Where(name => name is not null).All(names.Contains))
                {
                    return (sheet, rowNumber);
                }
            }
        }

        return null;
    }

    // Excel can keep a long number such as a barcode as 6.000000000001E+12. A code must keep all its digits.
    private static string Code(string? text)
    {
        var value = text?.Trim() ?? string.Empty;
        return value.Contains('E', StringComparison.OrdinalIgnoreCase)
            && decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && number == decimal.Truncate(number)
                ? number.ToString("0", CultureInfo.InvariantCulture)
                : value;
    }

    // "SALES\n\nTOTAL" and "Sales Total" are the same column.
    private static string? Normalize(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : Whitespace().Replace(name.Trim(), " ").ToUpperInvariant();

    private static DateOnly ReadDate(string text)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 20000 and < 80000)
        {
            return DateOnly.FromDateTime(DateTime.FromOADate(serial));
        }

        string[] formats = ["yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "dd/MM/yyyy", "yyyy/MM/dd", "dd-MM-yyyy"];
        return DateOnly.FromDateTime(DateTime.ParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None));
    }

    private static decimal Number(string text) =>
        text.Length == 0 ? 0m : decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private sealed record Layout(
        string Source,
        string Date,
        string StoreCode,
        string StoreName,
        string Product,
        string? Gtin,
        string Quantity,
        string Value,
        string Marker);

    private sealed class Group(string storeName)
    {
        public string StoreName { get; } = storeName;
        public string? Gtin { get; set; }
        public decimal Units { get; set; }
        public decimal Value { get; set; }
    }
}
