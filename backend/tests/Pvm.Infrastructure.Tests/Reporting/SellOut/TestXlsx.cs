using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace Pvm.Infrastructure.Tests.Reporting.SellOut;

/// <summary>
/// Writes a small .xlsx workbook for tests. Text cells go to the shared strings, as Excel does.
/// A cell value that is a number goes in as a number. Null is an empty cell.
/// </summary>
internal static class TestXlsx
{
    public static byte[] Build(params (string Sheet, object?[][] Rows)[] sheets)
    {
        var strings = new List<string>();
        int StringIndex(string value)
        {
            var index = strings.IndexOf(value);
            if (index < 0)
            {
                strings.Add(value);
                index = strings.Count - 1;
            }

            return index;
        }

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var sheetXml = sheets.Select(sheet => SheetXml(sheet.Rows, StringIndex)).ToList();

            Write(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                <Default Extension="xml" ContentType="application/xml"/>
                </Types>
                """);
            Write(zip, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" "
                + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>"
                + string.Concat(sheets.Select((sheet, index) =>
                    $"<sheet name=\"{SecurityElement.Escape(sheet.Sheet)}\" sheetId=\"{index + 1}\" r:id=\"rId{index + 1}\"/>"))
                + "</sheets></workbook>");
            Write(zip, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + string.Concat(sheets.Select((_, index) =>
                    $"<Relationship Id=\"rId{index + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{index + 1}.xml\"/>"))
                + "</Relationships>");
            for (var index = 0; index < sheetXml.Count; index++)
            {
                Write(zip, $"xl/worksheets/sheet{index + 1}.xml", sheetXml[index]);
            }

            Write(zip, "xl/sharedStrings.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + string.Concat(strings.Select(value => $"<si><t xml:space=\"preserve\">{SecurityElement.Escape(value)}</t></si>"))
                + "</sst>");
        }

        return stream.ToArray();
    }

    private static string SheetXml(object?[][] rows, Func<string, int> stringIndex)
    {
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (var row = 0; row < rows.Length; row++)
        {
            xml.Append(CultureInfo.InvariantCulture, $"<row r=\"{row + 1}\">");
            for (var column = 0; column < rows[row].Length; column++)
            {
                var reference = $"{ColumnName(column)}{row + 1}";
                switch (rows[row][column])
                {
                    case null:
                        break;
                    case string text:
                        xml.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" t=\"s\"><v>{stringIndex(text)}</v></c>");
                        break;
                    case IFormattable number:
                        xml.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\"><v>{number.ToString(null, CultureInfo.InvariantCulture)}</v></c>");
                        break;
                }
            }

            xml.Append("</row>");
        }

        return xml.Append("</sheetData></worksheet>").ToString();
    }

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        for (index++; index > 0; index = (index - 1) / 26)
        {
            name = (char)('A' + ((index - 1) % 26)) + name;
        }

        return name;
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
