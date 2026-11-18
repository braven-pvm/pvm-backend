using System.Text;
using Pvm.Application.Reporting.SellOut;
using Pvm.Infrastructure.Reporting.SellOut;

namespace Pvm.Infrastructure.Tests.Reporting.SellOut;

public sealed class MakroDispoParserTests
{
    private static readonly string[] Header =
    [
        "", "Vendor", "Vendor Name", "Vendor Prod Code", "P Term", "Future Promo", "Seq No", "Article", "",
        "Article Desc", "Barcode", "BMC", "BMC Description", "Class", "PBC", "Order Unit",
        "06-2026", "07-2026", "08-2026", "09-2026", "10-2026", "11-2026", "11-2025", "Curr Y/S",
        "UOM", "     Compo", "Site", "Site Name", "Status", "RP", "    SOH", "     SOO", "   SIT",
        "PR QTY", "   MAC", "Stock Margin", "List Price", "Net Cost",
    ];

    // Made-up values in the shape of the Makro Dispo export: tab-separated text, header on line 4.
    internal static string File(params string[] rows) =>
        "05.11.2026          Dynamic List Display\r\n\r\n\r\n"
        + string.Join('\t', Header) + "\r\n"
        + string.Join("\r\n", rows) + "\r\n";

    internal static string Row(
        string uom,
        string compo,
        string[] months,
        string site = "M01",
        string siteName = "Example Main Store",
        string article = "100000001",
        string barcode = "6000000000001",
        string soh = "0.000",
        string soo = "0.000",
        string sit = "0.000",
        string netCost = "10.00")
    {
        var cells = new string[Header.Length];
        Array.Fill(cells, "");
        void Set(string name, string value) => cells[Array.FindIndex(Header, header => header.Trim() == name)] = value;
        Set("Vendor", "V001");
        Set("Article", article);
        Set("Article Desc", "EXAMPLE BAR 45G");
        Set("Barcode", barcode);
        Set("Order Unit", "240.000");
        for (var index = 0; index < months.Length; index++)
        {
            cells[16 + index] = months[index];
        }

        Set("UOM", uom);
        Set("Compo", compo);
        Set("Site", site);
        Set("Site Name", siteName);
        Set("SOH", soh);
        Set("SOO", soo);
        Set("SIT", sit);
        Set("Net Cost", netCost);
        return string.Join('\t', cells);
    }

    private static readonly string[] NoSales = ["0", "0", "0", "0", "0", "0", "0", "0"];

    private static SellOutFile Make(string text, string name = "Makro Dispo Report PVM 05112026.xls") =>
        new(name, Encoding.Latin1.GetBytes(text), new DateOnly(2026, 11, 6));

    [Fact]
    public void Units_value_and_stock_add_up_over_all_units_of_measure_in_single_items()
    {
        var file = Make(File(
            Row("EA", "1.000", ["0", "0", "0", "0", "6", "1", "9", "0"], netCost: "10.00"),
            Row("SW", "20.000", ["0", "0", "0", "0", "2", "0", "0", "0"], soh: "3.000", netCost: "200.00", barcode: "6000000000002"),
            Row("CS", "240.000", ["0", "0", "0", "0", "0", "0", "0", "0"], soo: "1.000", netCost: "2,400.00", barcode: "6000000000003"),
            Row("PAL", "9,600.000", NoSales, barcode: ""),
            Row("**", "1.000", ["0", "0", "0", "0", "5", "0", "0", "0"], barcode: "", netCost: "0.00")));

        var result = new MakroDispoParser().Parse(file);

        Assert.Equal("makro-dispo", result.Source);
        Assert.Equal("makro", result.Retailer);
        Assert.Equal(new DateOnly(2026, 11, 5), result.FileDate);
        Assert.Equal(5, result.RowsRead);
        Assert.Equal(1, result.RowsIgnored);
        Assert.Empty(result.Issues);

        var october = Assert.Single(result.MonthRows, row => row.Month == 10);
        Assert.Equal(2026, october.Year);
        Assert.Equal("M01", october.StoreCode);
        Assert.Equal("Example Main Store", october.StoreName);
        Assert.Equal("100000001", october.ProductCode);
        Assert.Equal("6000000000001", october.Gtin);
        Assert.Equal(46m, october.UnitsEach); // 6 each + 2 shrink wraps of 20.
        Assert.Equal(460.00m, october.ValueExclVat); // 6 x 10.00 + 2 x 200.00.
        Assert.False(october.IsPartial);

        var stock = Assert.Single(result.StockRows);
        Assert.Equal(60m, stock.StockOnHand); // 3 shrink wraps of 20.
        Assert.Equal(240m, stock.StockOnOrder); // 1 case of 240.
        Assert.Equal(0m, stock.StockInTransit);
    }

    [Fact]
    public void The_six_months_are_read_the_month_of_the_file_is_partial_and_last_year_is_left_out()
    {
        var file = Make(File(Row("EA", "1.000", ["1", "2", "3", "4", "5", "6", "99", "0"])));

        var result = new MakroDispoParser().Parse(file);

        Assert.Equal(
            [new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 1),
             new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1)],
            result.Months);
        Assert.Equal([1m, 2m, 3m, 4m, 5m, 6m], result.MonthRows.OrderBy(row => row.Month).Select(row => row.UnitsEach));
        Assert.True(result.MonthRows.Single(row => row.Month == 11).IsPartial);
        Assert.DoesNotContain(result.MonthRows, row => row.Year == 2025);
    }

    [Fact]
    public void A_month_with_no_sales_gives_no_row_and_returns_count_as_negative_units()
    {
        var file = Make(File(Row("EA", "1.000", ["0", "0", "0", "0", "-2", "0", "0", "0"])));

        var result = new MakroDispoParser().Parse(file);

        var row = Assert.Single(result.MonthRows);
        Assert.Equal(10, row.Month);
        Assert.Equal(-2m, row.UnitsEach);
    }

    [Fact]
    public void Stores_and_articles_stay_apart_and_thousands_separators_are_read()
    {
        var file = Make(File(
            Row("EA", "1.000", ["0", "0", "0", "0", "1,250", "0", "0", "0"], site: "M01"),
            Row("EA", "1.000", ["0", "0", "0", "0", "3", "0", "0", "0"], site: "A02", siteName: "Other Store"),
            Row("EA", "1.000", ["0", "0", "0", "0", "4", "0", "0", "0"], site: "M01", article: "100000002", barcode: "6000000000009")));

        var result = new MakroDispoParser().Parse(file);

        Assert.Equal(3, result.MonthRows.Count);
        Assert.Equal(1250m, result.MonthRows.Single(row => row.StoreCode == "M01" && row.ProductCode == "100000001").UnitsEach);
        Assert.Equal("Other Store", result.MonthRows.Single(row => row.StoreCode == "A02").StoreName);
    }

    [Fact]
    public void A_row_with_an_unreadable_number_is_an_issue_and_the_other_rows_load()
    {
        var file = Make(File(
            Row("EA", "1.000", ["0", "0", "0", "0", "x", "0", "0", "0"]),
            Row("EA", "1.000", ["0", "0", "0", "0", "3", "0", "0", "0"], site: "A02")));

        var result = new MakroDispoParser().Parse(file);

        var issue = Assert.Single(result.Issues);
        Assert.Equal(5, issue.RowNumber);
        Assert.Contains("M01", issue.RawKey, StringComparison.Ordinal);
        Assert.Single(result.MonthRows);
    }

    [Fact]
    public void The_format_is_known_by_its_header()
    {
        var parser = new MakroDispoParser();

        Assert.True(parser.CanRead(Make(File(Row("EA", "1.000", NoSales)))));
        Assert.False(parser.CanRead(Make("Vendor Article Sales and Stock\r\n")));
        Assert.False(parser.CanRead(new SellOutFile("x.xlsx", [0x50, 0x4B, 0x03, 0x04], new DateOnly(2026, 11, 6))));
    }

    [Fact]
    public void A_file_without_the_month_columns_is_refused()
    {
        var text = File(Row("EA", "1.000", NoSales)).Replace("06-2026", "Month A", StringComparison.Ordinal)
            .Replace("07-2026", "Month B", StringComparison.Ordinal);

        Assert.Throws<SellOutFormatException>(() => new MakroDispoParser().Parse(Make(text)));
    }
}
