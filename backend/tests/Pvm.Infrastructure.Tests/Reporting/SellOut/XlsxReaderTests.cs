using Pvm.Application.Reporting.SellOut;
using Pvm.Infrastructure.Reporting.SellOut;

namespace Pvm.Infrastructure.Tests.Reporting.SellOut;

public sealed class XlsxReaderTests
{
    [Fact]
    public void Rows_come_back_with_shared_strings_numbers_and_empty_cells()
    {
        var content = TestXlsx.Build(
            ("First", [["Name", "Units"], ["Example", 12], [null, 3.5m]]),
            ("Second", [["Other"]]));

        using var reader = XlsxReader.Open(content);
        var rows = reader.ReadRows("First").ToList();

        Assert.Equal(["First", "Second"], reader.SheetNames);
        Assert.Equal([1, 2, 3], rows.Select(row => row.RowNumber));
        Assert.Equal((IEnumerable<string?>)["Name", "Units"], rows[0].Cells.AsEnumerable());
        Assert.Equal((IEnumerable<string?>)["Example", "12"], rows[1].Cells.AsEnumerable());
        Assert.Equal((IEnumerable<string?>)[null, "3.5"], rows[2].Cells.AsEnumerable());
    }

    [Fact]
    public void A_cell_far_to_the_right_keeps_its_column()
    {
        var row = new object?[30];
        row[0] = "A";
        row[27] = "AB";
        var content = TestXlsx.Build(("Sheet", [row]));

        using var reader = XlsxReader.Open(content);
        var cells = reader.ReadRows("Sheet").Single().Cells;

        Assert.Equal("A", cells[0]);
        Assert.Equal("AB", cells[27]);
        Assert.Null(cells[26]);
    }

    [Fact]
    public void A_file_that_is_not_a_workbook_is_refused()
    {
        Assert.False(XlsxReader.LooksLikeXlsx("Vendor\tArticle"u8.ToArray()));
        Assert.Throws<SellOutFormatException>(() => XlsxReader.Open("Vendor\tArticle"u8.ToArray()));
        Assert.Throws<SellOutFormatException>(() => XlsxReader.Open([0x50, 0x4B, 0x03, 0x04, 0x00, 0x00]));
    }

    [Fact]
    public void A_missing_sheet_is_refused()
    {
        using var reader = XlsxReader.Open(TestXlsx.Build(("Sheet", [["x"]])));

        Assert.Throws<SellOutFormatException>(() => reader.ReadRows("Other").ToList());
    }
}
