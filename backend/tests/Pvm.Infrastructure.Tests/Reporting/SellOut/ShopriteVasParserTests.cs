using Pvm.Application.Reporting.SellOut;
using Pvm.Infrastructure.Reporting.SellOut;

namespace Pvm.Infrastructure.Tests.Reporting.SellOut;

public sealed class ShopriteVasParserTests
{
    private static readonly string[] Months = ["May 2026 units", "Jun 2026 units", "Jul 2026 units", "Aug 2026 units", "Sep 2026 units", "Oct 2026 units"];

    // Made-up values in the shape of the portal report: two header rows, data from row 8.
    private static object?[] GroupHeader()
    {
        var row = new object?[34];
        row[13] = "# Sites";
        row[17] = "Sell Price (Incl)";
        row[18] = "POS Sales";
        row[27] = "All Store Stock";
        row[33] = "DC Stock";
        return row;
    }

    private static object?[] Header()
    {
        var names = new[]
        {
            "Vendor Subrange", "Site", "Department", "Catergory Group", "Category", "Sub-category", "Article Key", "Article",
            "PL Label 4", "Sell UOM", "Orderable UOM", "RP Type", "New", "Lstd", "Stk", "Ord Blk", "Active Stores", null,
        }.Concat(Months).Concat(
        [
            "Total Sales Units", "Curr ROS", "Latest Period Sales (Excl)", "Stock Value @MAC", "Stock Qty", "WOS",
            "Date Last Sold", "Date Last Ordered", "Date Last Receipted",
        ]);
        return names.Cast<object?>().Append(null).ToArray();
    }

    internal static object?[] Row(
        string site = "0001-EXAMPLE STORE ONE",
        string articleKey = "10000001",
        int[]? units = null,
        decimal latestValue = 120.50m,
        int stockQty = 7,
        decimal stockValue = 70.00m,
        decimal weeks = 2.5m,
        int dcStock = 400,
        string sellUom = "EA-1")
    {
        var row = new object?[34];
        row[1] = site;
        row[6] = articleKey;
        row[7] = "EXAMPLE BAR 45G, FLAVOUR";
        row[9] = sellUom;
        row[17] = 25.99m;
        var values = units ?? [1, 2, 3, 4, 5, 6];
        for (var index = 0; index < 6; index++)
        {
            row[18 + index] = values[index];
        }

        row[24] = values.Sum();
        row[26] = latestValue;
        row[27] = stockValue;
        row[28] = stockQty;
        row[29] = weeks;
        row[33] = dcStock;
        return row;
    }

    internal static byte[] File(params object?[][] rows)
    {
        object?[][] view =
        [
            ["Vendor Article Sales and Stock"],
            ["For Last 6 Months, Ending Oct 2026"],
            [],
            [],
            [],
            GroupHeader(),
            Header(),
            .. rows,
        ];
        return TestXlsx.Build(
            ("Parameter Selection", [["Selection"], ["Date requested: 2026-11-11"]]),
            ("Consolidated View", [["Vendor Article Sales and Stock"]]),
            ("Separate View", view));
    }

    private static SellOutFile Make(byte[] content) => new("vnd-art-sas_11-11-2026_example.xlsx", content, new DateOnly(2026, 11, 12));

    [Fact]
    public void Each_store_and_article_gives_six_months_the_latest_value_and_the_stock()
    {
        var result = new ShopriteVasParser().Parse(Make(File(Row())));

        Assert.Equal("shoprite-vas", result.Source);
        Assert.Equal("shoprite-group", result.Retailer);
        Assert.Equal(new DateOnly(2026, 11, 11), result.FileDate);
        Assert.Equal(new DateOnly(2026, 5, 1), result.Months[0]);
        Assert.Equal(new DateOnly(2026, 10, 1), result.Months[^1]);
        Assert.Equal(1, result.RowsRead);
        Assert.Empty(result.Issues);

        Assert.Equal([1m, 2m, 3m, 4m, 5m, 6m], result.MonthRows.OrderBy(row => row.Month).Select(row => row.UnitsEach));
        Assert.All(result.MonthRows, row =>
        {
            Assert.Equal("0001", row.StoreCode);
            Assert.Equal("EXAMPLE STORE ONE", row.StoreName);
            Assert.Equal("10000001", row.ProductCode);
            Assert.Null(row.Gtin);
            Assert.False(row.IsPartial);
        });
        Assert.Equal(120.50m, result.MonthRows.Single(row => row.Month == 10).ValueExclVat);
        Assert.Null(result.MonthRows.Single(row => row.Month == 9).ValueExclVat);

        var stock = Assert.Single(result.StockRows);
        Assert.Equal(7m, stock.StockOnHand);
        Assert.Equal(70.00m, stock.StockValue);
        Assert.Equal(2.5m, stock.WeeksOfStock);
        Assert.Equal(400m, stock.DcStock);
    }

    [Fact]
    public void Store_codes_with_letters_are_kept_and_months_with_no_sales_give_no_row()
    {
        var result = new ShopriteVasParser().Parse(Make(File(
            Row(site: "G123-EXAMPLE STORE TWO", units: [0, 0, 0, 0, 0, 3]))));

        var row = Assert.Single(result.MonthRows);
        Assert.Equal("G123", row.StoreCode);
        Assert.Equal(10, row.Month);
    }

    [Fact]
    public void A_sell_unit_of_more_than_one_item_is_counted_in_single_items()
    {
        var result = new ShopriteVasParser().Parse(Make(File(Row(units: [0, 0, 0, 0, 0, 3], sellUom: "PK-20", stockQty: 2))));

        Assert.Equal(60m, Assert.Single(result.MonthRows).UnitsEach);
        Assert.Equal(40m, Assert.Single(result.StockRows).StockOnHand);
    }

    [Fact]
    public void Empty_rows_after_the_data_are_not_counted()
    {
        var result = new ShopriteVasParser().Parse(Make(File(Row(), new object?[34], Row(site: "0002-EXAMPLE STORE THREE"))));

        Assert.Equal(2, result.RowsRead);
        Assert.Equal(2, result.StockRows.Count);
    }

    [Fact]
    public void A_row_with_an_unreadable_number_is_an_issue()
    {
        var bad = Row(site: "0002-EXAMPLE STORE THREE");
        bad[20] = "n/a";

        var result = new ShopriteVasParser().Parse(Make(File(Row(), bad)));

        var issue = Assert.Single(result.Issues);
        Assert.Equal(9, issue.RowNumber);
        Assert.Contains("0002", issue.RawKey, StringComparison.Ordinal);
        Assert.Single(result.StockRows);
    }

    [Fact]
    public void The_format_is_known_by_its_title_and_sheet()
    {
        var parser = new ShopriteVasParser();

        Assert.True(parser.CanRead(Make(File(Row()))));
        Assert.False(parser.CanRead(Make(TestXlsx.Build(("Sheet1", [["Something else"]])))));
        Assert.False(parser.CanRead(Make(MakroDispoParserTestsFile())));
    }

    [Fact]
    public void A_report_without_six_month_columns_is_refused()
    {
        object?[][] view = [["Vendor Article Sales and Stock"], ["For Last 6 Months, Ending Oct 2026"], [], [], [], GroupHeader(), ["Site", "Article Key"]];
        var content = TestXlsx.Build(("Separate View", view));

        Assert.Throws<SellOutFormatException>(() => new ShopriteVasParser().Parse(Make(content)));
    }

    private static byte[] MakroDispoParserTestsFile() =>
        System.Text.Encoding.Latin1.GetBytes(MakroDispoParserTests.File(MakroDispoParserTests.Row("EA", "1.000", ["0", "0", "0", "0", "0", "0", "0", "0"])));
}
