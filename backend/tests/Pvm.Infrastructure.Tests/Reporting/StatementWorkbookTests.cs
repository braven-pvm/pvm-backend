using Pvm.Application.Reporting.Statements;
using Pvm.Infrastructure.Reporting;
using Pvm.Infrastructure.Reporting.SellOut;
using Pvm.Infrastructure.Reporting.Statements;

namespace Pvm.Infrastructure.Tests.Reporting;

public sealed class StatementWorkbookTests
{
    private static readonly DateOnly October = new(2026, 10, 1);

    // Made-up statement: two areas with a commission each, Makro in the first, and the Dis-Chem sheet.
    private static MerchandiserStatement Example() => new(
        "Example Merchandising",
        "EXAMPLE MERCHANDISING",
        October,
        October,
        [
            new StatementArea(
                "North",
                [new StatementLine("Retail", 300m, [new StatementCustomer("C1", "STORE ONE", 200m), new StatementCustomer("C2", "STORE TWO", 100m)])],
                50m,
                [new StatementMakroStore("Alpha Main Store", 50m)],
                350m,
                5m,
                17.50m),
            new StatementArea("South", [new StatementLine("Retail", 100m, [new StatementCustomer("C3", "STORE THREE", 100m)])], 0m, [], 100m, 5m, 5m),
        ],
        450m,
        5m,
        22.50m,
        null,
        false,
        new DisChemYearOnYear(
            [new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), October],
            [new DisChemStoreYears("DIS-CHEM EXAMPLE S001", [10m, 20m, 30m], [10m, 10m, 10m], 100m)]));

    private static List<(int Row, string?[] Cells)> Read(byte[] content, string sheet)
    {
        using var reader = XlsxReader.Open(content);
        return reader.ReadRows(sheet).ToList();
    }

    [Fact]
    public void The_workbook_has_a_summary_a_detail_sheet_for_each_area_a_makro_sheet_and_the_dis_chem_sheet()
    {
        using var reader = XlsxReader.Open(StatementWorkbook.Write(Example()));

        Assert.Equal(["Summary", "North detail", "South detail", "Makro", "Dis-Chem"], reader.SheetNames);
    }

    [Fact]
    public void The_summary_gives_each_area_its_total_and_commission_then_the_grand_total_and_commission()
    {
        var rows = Read(StatementWorkbook.Write(Example()), "Summary");
        string?[] Find(string label) => rows.Single(row => row.Cells.Length > 0 && row.Cells[0] == label).Cells;

        Assert.Equal("EXAMPLE MERCHANDISING", rows[0].Cells[0]);
        Assert.Contains(rows, row => row.Cells.Length > 1 && row.Cells[0] == "NORTH" && row.Cells[1] == "Oct 2026");
        Assert.Equal("50", rows.First(row => row.Cells.Length > 0 && row.Cells[0] == "Makro").Cells[1]);
        Assert.Equal(["350", "100"], rows.Where(row => row.Cells.Length > 1 && row.Cells[0] == "Total").Select(row => row.Cells[1]));
        Assert.Equal("450", Find("Grand total of all areas")[1]);
        Assert.Equal("22.50", rows.Last().Cells[1]);
        Assert.Equal("Commission 5%", rows.Last().Cells[0]);
    }

    [Fact]
    public void A_detail_sheet_lists_each_line_with_its_customers()
    {
        var rows = Read(StatementWorkbook.Write(Example()), "North detail");

        Assert.Equal(["Retail", "STORE ONE", "STORE TWO", "Grand total"], rows.Skip(2).Select(row => row.Cells[0]));
        Assert.Equal(["300", "200", "100", "300"], rows.Skip(2).Select(row => row.Cells[1]));
    }

    [Fact]
    public void A_retainer_company_shows_the_retainer_instead_of_a_commission()
    {
        var statement = Example() with { Areas = [Example().Areas[1] with { Commission = null, CommissionRatePercent = null }], GrandTotal = 100m, RatePercent = null, Commission = null, RetainerAmount = 10000m, DisChem = null };

        var rows = Read(StatementWorkbook.Write(statement), "Summary");

        Assert.Equal(("Retainer", "10000"), (rows.Last().Cells[0], rows.Last().Cells[1]));
        Assert.DoesNotContain(rows, row => row.Cells.Length > 0 && row.Cells[0] == "Grand total of all areas");
    }

    [Fact]
    public void The_dis_chem_sheet_gives_both_years_and_the_growth()
    {
        var rows = Read(StatementWorkbook.Write(Example()), "Dis-Chem");

        Assert.Equal((IEnumerable<string?>)["Store", "Aug 2025", "Sep 2025", "Oct 2025", "Total", "Aug 2026", "Sep 2026", "Oct 2026", "Total", "Growth"], rows[1].Cells.AsEnumerable());
        Assert.Equal((IEnumerable<string?>)["DIS-CHEM EXAMPLE S001", "10", "10", "10", "30", "10", "20", "30", "60", "1"], rows[2].Cells.AsEnumerable());
    }

    [Fact]
    public void The_file_name_names_the_company_and_the_period()
    {
        Assert.Equal("Example Merchandising Oct 2026.xlsx", StatementWorkbook.FileName(Example()));
        Assert.Equal("Example Merchandising May 2026 - Sep 2026.xlsx", StatementWorkbook.FileName(Example() with { FirstMonth = new DateOnly(2026, 5, 1), LastMonth = new DateOnly(2026, 9, 1) }));
    }

    [Fact]
    public void Text_with_xml_characters_and_long_sheet_names_are_written_safely()
    {
        var content = XlsxWriter.Write([new XlsxSheet("A very long sheet name that Excel would refuse [x]", [[XlsxCell.Of("P&P <Family> \"Mini\"")]])]);

        using var reader = XlsxReader.Open(content);
        var name = Assert.Single(reader.SheetNames);
        Assert.True(name.Length <= 31);
        Assert.Equal("P&P <Family> \"Mini\"", reader.ReadRows(name).Single().Cells[0]);
    }
}
