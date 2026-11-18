using Pvm.Application.Reporting.SellOut;
using Pvm.Infrastructure.Reporting.SellOut;

namespace Pvm.Infrastructure.Tests.Reporting.SellOut;

public sealed class WholesalerListParserTests
{
    // Excel stores a date as the number of days after 30 December 1899.
    private static double ExcelDate(int year, int month, int day) => new DateTime(year, month, day).ToOADate();

    private static SellOutFile Make(byte[] content) => new("wholesaler.xlsx", content, new DateOnly(2026, 5, 6));

    // Made-up values in the shape of the CJ list: header on row 1, a total row at the end.
    private static byte[] CjFile(params object?[][] rows) => TestXlsx.Build(("CJ Jan - April",
    [
        ["Suppacnt", "Suppdiv", "Date", "ACCOUNT", "NAME", "SALESREP", "CODE", "DESCR", "PQTY", "UPRICEEX", "VALUE"],
        .. rows,
        [null, null, null, null, null, null, null, null, 999, null, 9999],
    ]));

    private static object?[] CjRow(string account, string code, double date, int quantity, decimal value) =>
        ["PVM", "PVM", date, account, $"PHARMACY {account}", "REP A", code, "EXAMPLE BAR 45G", quantity, value / Math.Max(1, quantity), value];

    [Fact]
    public void Lines_add_up_to_customer_product_and_month()
    {
        var file = Make(CjFile(
            CjRow("1001", "500001", ExcelDate(2026, 2, 3), 10, 100m),
            CjRow("1001", "500001", ExcelDate(2026, 2, 20), 5, 50m),
            CjRow("1001", "500001", ExcelDate(2026, 3, 1), -2, -20m),
            CjRow("1002", "500001", ExcelDate(2026, 2, 3), 1, 10m)));

        var result = WholesalerListParser.Cj().Parse(file);

        Assert.Equal("wholesaler-cj", result.Source);
        Assert.Equal("wholesaler-cj", result.Retailer);
        Assert.Equal(4, result.RowsRead);
        Assert.Equal(1, result.RowsIgnored);
        Assert.Empty(result.Issues);
        Assert.Empty(result.StockRows);
        Assert.Equal([new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1)], result.Months);

        var february = result.MonthRows.Single(row => row.StoreCode == "1001" && row.Month == 2);
        Assert.Equal(15m, february.UnitsEach);
        Assert.Equal(150m, february.ValueExclVat);
        Assert.Equal("PHARMACY 1001", february.StoreName);
        Assert.Equal("500001", february.ProductCode);
        Assert.Equal(-2m, result.MonthRows.Single(row => row.StoreCode == "1001" && row.Month == 3).UnitsEach);
        Assert.Equal(3, result.MonthRows.Count);
    }

    [Fact]
    public void Alpha_pharm_gives_the_barcode_and_the_party_number()
    {
        var file = Make(TestXlsx.Build(("Alpha Pharm Feb-Apr",
        [
            ["ITEM_NUMBER", "DESCRIPTION", "NAPPI_CODE", "BAR_CODE", "SCHEDULING", "SUPPLIER", "INV_DATE", "INV_NUMBER",
             "ORDER_NUMBER", "QUANTITY_INVOICED", "EXTENDED_AMOUNT", "ITEM_CLASS_CODE", "PARTY_NAME", "PARTY_NUMBER"],
            [700001, "EXAMPLE BAR", 0, 6000000000001, "S0", "PVM", ExcelDate(2026, 4, 2), 1, 1, 3, 75.5m, "CLASS", "EXAMPLE PHARMACY", 300001],
        ])));

        var result = WholesalerListParser.AlphaPharm().Parse(file);

        var row = Assert.Single(result.MonthRows);
        Assert.Equal("wholesaler-alphapharm", result.Source);
        Assert.Equal("300001", row.StoreCode);
        Assert.Equal("EXAMPLE PHARMACY", row.StoreName);
        Assert.Equal("700001", row.ProductCode);
        Assert.Equal("6000000000001", row.Gtin);
        Assert.Equal(3m, row.UnitsEach);
        Assert.Equal(75.5m, row.ValueExclVat);
    }

    [Fact]
    public void Header_names_with_line_breaks_are_read_and_a_list_without_account_uses_the_customer_name()
    {
        var file = Make(TestXlsx.Build(("Arrie Nel Feb-April",
        [
            ["CUSTOMER NAME", "INV NO.", "DATE", "PRODUCT", "DESCRIPTION", "QTY", "SALES\n\nTOTAL", "SALESREP"],
            [" EXAMPLE PHARMACY ", "INV0001", ExcelDate(2026, 2, 9), 900001, "EXAMPLE BAR", 4, 120, "REP B"],
        ])));

        var result = WholesalerListParser.ArrieNel().Parse(file);

        var row = Assert.Single(result.MonthRows);
        Assert.Equal("EXAMPLE PHARMACY", row.StoreCode);
        Assert.Equal("EXAMPLE PHARMACY", row.StoreName);
        Assert.Equal(120m, row.ValueExclVat);
    }

    [Fact]
    public void Topmed_reads_its_own_columns()
    {
        var file = Make(TestXlsx.Build(("TOPMED Feb - Apr",
        [
            ["ACCOUNT", "NAME", "TRANS NO", "DATE", "PRODUCT", "DESCRIPTION", "QTY", "SALES\nTOTAL", "SALESREP"],
            ["400001", "EXAMPLE PHARMACY", "INV00001", ExcelDate(2026, 3, 9), "800001", "EXAMPLE BAR", 2, 60, "REP C"],
        ])));

        var row = Assert.Single(WholesalerListParser.TopMed().Parse(file).MonthRows);

        Assert.Equal("400001", row.StoreCode);
        Assert.Equal(3, row.Month);
        Assert.Equal(60m, row.ValueExclVat);
    }

    [Fact]
    public void A_line_with_an_unreadable_date_is_an_issue()
    {
        var file = Make(CjFile(
            ["PVM", "PVM", "not a date", "1001", "PHARMACY", "REP", "500001", "BAR", 1, 10, 10],
            CjRow("1002", "500001", ExcelDate(2026, 2, 3), 1, 10m)));

        var result = WholesalerListParser.Cj().Parse(file);

        var issue = Assert.Single(result.Issues);
        Assert.Equal(2, issue.RowNumber);
        Assert.Single(result.MonthRows);
    }

    [Fact]
    public void Each_wholesaler_knows_only_its_own_list()
    {
        var cj = Make(CjFile(CjRow("1001", "500001", ExcelDate(2026, 2, 3), 1, 10m)));

        Assert.True(WholesalerListParser.Cj().CanRead(cj));
        Assert.False(WholesalerListParser.TopMed().CanRead(cj));
        Assert.False(WholesalerListParser.AlphaPharm().CanRead(cj));
        Assert.False(WholesalerListParser.ArrieNel().CanRead(cj));
        Assert.False(new ShopriteVasParser().CanRead(cj));
        Assert.False(WholesalerListParser.Cj().CanRead(Make("ACCOUNT\tNAME"u8.ToArray())));
    }

    [Fact]
    public void All_returns_the_four_wholesaler_parsers()
    {
        Assert.Equal(
            ["wholesaler-alphapharm", "wholesaler-arrienel", "wholesaler-cj", "wholesaler-topmed"],
            WholesalerListParser.All().Select(parser => parser.Source).Order());
    }
}
