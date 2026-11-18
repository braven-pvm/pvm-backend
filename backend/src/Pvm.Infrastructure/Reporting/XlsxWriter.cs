using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace Pvm.Infrastructure.Reporting;

/// <summary>The look of a cell.</summary>
public enum XlsxStyle
{
    Normal = 0,
    Bold = 1,
    Money = 2,
    BoldMoney = 3,
    Percent = 4,
}

/// <summary>A cell: text, a number, or a formula with the value that it gives.</summary>
public sealed record XlsxCell(string? Text = null, decimal? Number = null, string? Formula = null, XlsxStyle Style = XlsxStyle.Normal)
{
    public static XlsxCell Of(string text, XlsxStyle style = XlsxStyle.Normal) => new(Text: text, Style: style);

    public static XlsxCell Of(decimal number, XlsxStyle style = XlsxStyle.Money) => new(Number: number, Style: style);

    /// <summary>A formula, such as "SUM(B2:B5)", with the value it gives, so that a reader with no calculation sees it.</summary>
    public static XlsxCell Sum(string formula, decimal value, XlsxStyle style = XlsxStyle.BoldMoney) => new(Number: value, Formula: formula, Style: style);
}

/// <summary>A sheet: rows of cells from column A, and the widths of the columns in characters.</summary>
public sealed record XlsxSheet(string Name, IReadOnlyList<IReadOnlyList<XlsxCell?>> Rows, IReadOnlyList<double>? ColumnWidths = null);

/// <summary>
/// Writes a small .xlsx workbook: shared strings, numbers, formulas with their values, and five
/// styles. An .xlsx file is a zip of XML parts, so the writer needs no package.
/// </summary>
public static class XlsxWriter
{
    public static byte[] Write(IReadOnlyList<XlsxSheet> sheets)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        if (sheets.Count == 0)
        {
            throw new ArgumentException("A workbook needs at least one sheet.", nameof(sheets));
        }

        var strings = new List<string>();
        var stringIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        int IndexOf(string text)
        {
            if (!stringIndex.TryGetValue(text, out var index))
            {
                index = strings.Count;
                strings.Add(text);
                stringIndex[text] = index;
            }

            return index;
        }

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var sheetXml = sheets.Select(sheet => SheetXml(sheet, IndexOf)).ToList();
            Part(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
                + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
                + string.Concat(sheets.Select((_, i) => $"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"))
                + "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>"
                + "<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>"
                + "</Types>");
            Part(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Part(zip, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" "
                + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>"
                + string.Concat(sheets.Select((sheet, i) => $"<sheet name=\"{Escape(SheetName(sheet.Name))}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>"))
                + "</sheets><calcPr fullCalcOnLoad=\"1\"/></workbook>");
            Part(zip, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + string.Concat(sheets.Select((_, i) => $"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>"))
                + $"<Relationship Id=\"rId{sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>"
                + $"<Relationship Id=\"rId{sheets.Count + 2}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" Target=\"sharedStrings.xml\"/>"
                + "</Relationships>");
            for (var i = 0; i < sheetXml.Count; i++)
            {
                Part(zip, $"xl/worksheets/sheet{i + 1}.xml", sheetXml[i]);
            }

            // The cell styles, in the order of XlsxStyle: normal, bold, money, bold money, percent.
            Part(zip, "xl/styles.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"#,##0.00\"/></numFmts>"
                + "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>"
                + "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>"
                + "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>"
                + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
                + "<cellXfs count=\"5\">"
                + "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>"
                + "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>"
                + "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>"
                + "<xf numFmtId=\"164\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\"/>"
                + "<xf numFmtId=\"10\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>"
                + "</cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");
            Part(zip, "xl/sharedStrings.xml",
                $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"{strings.Count}\" uniqueCount=\"{strings.Count}\">"
                + string.Concat(strings.Select(text => $"<si><t xml:space=\"preserve\">{Escape(text)}</t></si>"))
                + "</sst>");
        }

        return stream.ToArray();
    }

    /// <summary>The column letters of a 0-based column: 0 is A, 27 is AB.</summary>
    public static string Column(int index)
    {
        var name = string.Empty;
        for (index++; index > 0; index = (index - 1) / 26)
        {
            name = (char)('A' + ((index - 1) % 26)) + name;
        }

        return name;
    }

    private static string SheetXml(XlsxSheet sheet, Func<string, int> stringIndex)
    {
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        if (sheet.ColumnWidths is { Count: > 0 } widths)
        {
            xml.Append("<cols>");
            for (var i = 0; i < widths.Count; i++)
            {
                xml.Append(CultureInfo.InvariantCulture, $"<col min=\"{i + 1}\" max=\"{i + 1}\" width=\"{widths[i]}\" customWidth=\"1\"/>");
            }

            xml.Append("</cols>");
        }

        xml.Append("<sheetData>");
        for (var r = 0; r < sheet.Rows.Count; r++)
        {
            xml.Append(CultureInfo.InvariantCulture, $"<row r=\"{r + 1}\">");
            for (var c = 0; c < sheet.Rows[r].Count; c++)
            {
                if (sheet.Rows[r][c] is not { } cell)
                {
                    continue;
                }

                var reference = $"{Column(c)}{r + 1}";
                var style = (int)cell.Style;
                if (cell.Formula is not null)
                {
                    xml.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" s=\"{style}\"><f>{Escape(cell.Formula)}</f><v>{(cell.Number ?? 0m).ToString(CultureInfo.InvariantCulture)}</v></c>");
                }
                else if (cell.Number is { } number)
                {
                    xml.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" s=\"{style}\"><v>{number.ToString(CultureInfo.InvariantCulture)}</v></c>");
                }
                else if (cell.Text is { } text)
                {
                    xml.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" s=\"{style}\" t=\"s\"><v>{stringIndex(text)}</v></c>");
                }
            }

            xml.Append("</row>");
        }

        return xml.Append("</sheetData></worksheet>").ToString();
    }

    // Excel allows 31 characters and no : \ / ? * [ ] in a sheet name.
    private static string SheetName(string name)
    {
        var clean = new string(name.Select(ch => ":\\/?*[]".Contains(ch, StringComparison.Ordinal) ? '-' : ch).ToArray()).Trim();
        return clean.Length <= 31 ? clean : clean[..31];
    }

    private static string Escape(string text) => SecurityElement.Escape(text) ?? string.Empty;

    private static void Part(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
