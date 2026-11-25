using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace Pvm.Infrastructure.Reporting.SalesReport;

/// <summary>A line of text above the table of the workbook, such as "Period: Mar 2026 to Nov 2026".</summary>
public sealed record SalesWorkbookNote(string Label, string Text);

/// <summary>
/// Writes a sales pivot as an Excel workbook (.xlsx): the title and the filters at the top, then
/// a flat table. Each item of the deepest level has one row, and each level has its own column,
/// such as Rep, then Customer. There are no subtotal rows and no closed groups, so a person sorts,
/// filters and pivots the sheet as it is. The headers and the level columns stay in view.
/// The file is Office Open XML in a zip, written here, so the API needs no Excel library.
/// </summary>
public static class SalesPivotWorkbook
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // The cell styles of styles.xml, by their index.
    private const int Text = 0;
    private const int BoldText = 1;
    private const int Figure = 2;
    private const int BoldFigure = 3;
    private const int Percent = 4;
    private const int BoldPercent = 5;
    private const int Header = 6;
    private const int HeaderRight = 7;
    private const int Title = 8;
    private const int Note = 9;

    public static byte[] Write(SalesPivot pivot, string title, IReadOnlyList<SalesWorkbookNote> notes, IReadOnlyList<string> levelNames)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", ContentTypes);
            Add(zip, "_rels/.rels", RootRelations);
            Add(zip, "xl/workbook.xml", Workbook);
            Add(zip, "xl/_rels/workbook.xml.rels", WorkbookRelations);
            Add(zip, "xl/styles.xml", Styles);
            Add(zip, "xl/worksheets/sheet1.xml", Sheet(pivot, title, notes, levelNames));
        }

        return stream.ToArray();
    }

    private static string Sheet(SalesPivot pivot, string title, IReadOnlyList<SalesWorkbookNote> notes, IReadOnlyList<string> levelNames)
    {
        var compare = pivot.Totals.PreviousTotal is not null;
        var levels = Math.Max(levelNames.Count, 1);
        var headers = new List<string>(levelNames);
        headers.AddRange(pivot.Columns.Select(column => column.Label));
        headers.Add("Total");
        if (compare)
        {
            headers.Add("A year before");
            headers.Add("Change");
        }

        var rows = new StringBuilder();
        var number = 0;
        void Row(IEnumerable<string> cells)
        {
            number++;
            rows.Append(CultureInfo.InvariantCulture, $"<row r=\"{number}\">").Append(string.Concat(cells)).Append("</row>");
        }

        Row([TextCell(1, 1, title, Title)]);
        foreach (var note in notes)
        {
            Row([TextCell(number + 1, 1, $"{note.Label}: {note.Text}", Note)]);
        }

        Row([]);
        var headerRow = number + 1;
        Row(headers.Select((header, index) => TextCell(headerRow, index + 1, header, index < levels ? Header : HeaderRight)));

        // The rows come in tree order, so the path of a row is the labels of the rows above it.
        var path = new string[levels];
        foreach (var row in pivot.Rows)
        {
            path[row.Level] = row.Label;
            if (row.Level != levels - 1)
            {
                continue;
            }

            var cells = new List<string>();
            cells.AddRange(path.Select((label, index) => TextCell(number + 1, index + 1, label ?? string.Empty, Text)));
            cells.AddRange(Figures(number + 1, levels + 1, row.Values, row.Total, row.PreviousTotal, bold: false, compare));
            Row(cells);
        }

        var lastDataRow = number;
        var totals = new List<string> { TextCell(number + 1, 1, "Total", BoldText) };
        totals.AddRange(Figures(number + 1, levels + 1, pivot.Totals.Values, pivot.Totals.Total, pivot.Totals.PreviousTotal, bold: true, compare));
        Row(totals);

        var columns = new StringBuilder("<cols>");
        columns.Append(CultureInfo.InvariantCulture, $"<col min=\"1\" max=\"{levels}\" width=\"34\" customWidth=\"1\"/>");
        columns.Append(CultureInfo.InvariantCulture, $"<col min=\"{levels + 1}\" max=\"{headers.Count}\" width=\"14\" customWidth=\"1\"/></cols>");
        // The filter buttons cover the header and the item rows, not the total.
        var filter = $"<autoFilter ref=\"A{headerRow}:{ColumnName(headers.Count)}{Math.Max(lastDataRow, headerRow)}\"/>";
        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
            <dimension ref="A1:{ColumnName(headers.Count)}{number}"/>
            <sheetViews><sheetView workbookViewId="0"><pane xSplit="{levels}" ySplit="{headerRow}" topLeftCell="{ColumnName(levels + 1)}{headerRow + 1}" activePane="bottomRight" state="frozen"/></sheetView></sheetViews>
            <sheetFormatPr defaultRowHeight="15"/>
            {columns}
            <sheetData>{rows}</sheetData>
            {filter}
            </worksheet>
            """;
    }

    private static IEnumerable<string> Figures(int row, int firstColumn, IReadOnlyList<decimal> values, decimal total, decimal? previous, bool bold, bool compare)
    {
        var column = firstColumn;
        foreach (var value in values)
        {
            yield return NumberCell(row, column++, value, bold ? BoldFigure : Figure);
        }

        yield return NumberCell(row, column++, total, BoldFigure);
        if (compare)
        {
            yield return NumberCell(row, column++, previous ?? 0, bold ? BoldFigure : Figure);
            // The change is empty with nothing to compare with, as "New" on the pages.
            if (previous is > 0)
            {
                yield return NumberCell(row, column, (total - previous.Value) / previous.Value, bold ? BoldPercent : Percent);
            }
        }
    }

    private static string TextCell(int row, int column, string text, int style) =>
        $"<c r=\"{ColumnName(column)}{row}\" t=\"inlineStr\" s=\"{style}\"><is><t xml:space=\"preserve\">{Escape(text)}</t></is></c>";

    private static string NumberCell(int row, int column, decimal value, int style) =>
        $"<c r=\"{ColumnName(column)}{row}\" s=\"{style}\"><v>{value.ToString("G29", CultureInfo.InvariantCulture)}</v></c>";

    /// <summary>1 is A, 27 is AA.</summary>
    public static string ColumnName(int column)
    {
        var name = string.Empty;
        while (column > 0)
        {
            var rest = (column - 1) % 26;
            name = (char)('A' + rest) + name;
            column = (column - rest - 1) / 26;
        }

        return name;
    }

    /// <summary>XML text, without the control characters that XML does not allow.</summary>
    private static string Escape(string text) =>
        SecurityElement.Escape(new string([.. text.Where(character => character is '\t' or '\n' or '\r' || character >= ' ')])) ?? string.Empty;

    private static void Add(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content.Trim());
    }

    private const string ContentTypes = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
        <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
        <Default Extension="xml" ContentType="application/xml"/>
        <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
        <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
        <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
        </Types>
        """;

    private const string RootRelations = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
        <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private const string Workbook = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
        <sheets><sheet name="Sales" sheetId="1" r:id="rId1"/></sheets>
        </workbook>
        """;

    private const string WorkbookRelations = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
        <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
        <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
        </Relationships>
        """;

    // Fonts: 0 normal, 1 bold, 2 title, 3 grey note. Fills: 0 and 1 are required, 2 the header grey.
    // Number formats: 3 is "#,##0", 164 is "0.0%".
    private const string Styles = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
        <numFmts count="1"><numFmt numFmtId="164" formatCode="0.0%"/></numFmts>
        <fonts count="4">
        <font><sz val="11"/><name val="Calibri"/></font>
        <font><b/><sz val="11"/><name val="Calibri"/></font>
        <font><b/><sz val="14"/><name val="Calibri"/></font>
        <font><sz val="10"/><color rgb="FF5B6660"/><name val="Calibri"/></font>
        </fonts>
        <fills count="3">
        <fill><patternFill patternType="none"/></fill>
        <fill><patternFill patternType="gray125"/></fill>
        <fill><patternFill patternType="solid"><fgColor rgb="FFEEF1EF"/><bgColor indexed="64"/></patternFill></fill>
        </fills>
        <borders count="2">
        <border><left/><right/><top/><bottom/><diagonal/></border>
        <border><left/><right/><top/><bottom style="thin"><color rgb="FF848F89"/></bottom><diagonal/></border>
        </borders>
        <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
        <cellXfs count="15">
        <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
        <xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/>
        <xf numFmtId="3" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
        <xf numFmtId="3" fontId="1" fillId="0" borderId="0" xfId="0" applyNumberFormat="1" applyFont="1"/>
        <xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
        <xf numFmtId="164" fontId="1" fillId="0" borderId="0" xfId="0" applyNumberFormat="1" applyFont="1"/>
        <xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1"/>
        <xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right"/></xf>
        <xf numFmtId="0" fontId="2" fillId="0" borderId="0" xfId="0" applyFont="1"/>
        <xf numFmtId="0" fontId="3" fillId="0" borderId="0" xfId="0" applyFont="1"/>
        <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment indent="1"/></xf>
        <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment indent="2"/></xf>
        <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment indent="3"/></xf>
        <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment indent="4"/></xf>
        <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment indent="5"/></xf>
        </cellXfs>
        <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
        </styleSheet>
        """;
}
