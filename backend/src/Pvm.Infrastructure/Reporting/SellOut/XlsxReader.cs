using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Pvm.Application.Reporting.SellOut;

namespace Pvm.Infrastructure.Reporting.SellOut;

/// <summary>
/// Reads the cell values of an .xlsx workbook. An .xlsx file is a zip of XML parts, so the
/// reader needs no package. It reads values only: no formulas, styles or dates. A number comes
/// back as its invariant text, as Excel stores it.
/// </summary>
public sealed class XlsxReader : IDisposable
{
    private const string MainNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";

    // A sell-out file is small. The limit stops a zip bomb.
    private const long MaxPartBytes = 200L * 1024 * 1024;

    private readonly ZipArchive _zip;
    private readonly Dictionary<string, string> _sheetParts;
    private readonly List<string> _sharedStrings;

    private XlsxReader(ZipArchive zip)
    {
        _zip = zip;
        _sheetParts = ReadSheetParts();
        _sharedStrings = ReadSharedStrings();
    }

    /// <summary>True if the content starts as a zip file does.</summary>
    public static bool LooksLikeXlsx(byte[] content) =>
        content is { Length: > 4 } && content[0] == 0x50 && content[1] == 0x4B && content[2] == 0x03 && content[3] == 0x04;

    public static XlsxReader Open(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!LooksLikeXlsx(content))
        {
            throw new SellOutFormatException("The file is not an .xlsx workbook.");
        }

        try
        {
            return new XlsxReader(new ZipArchive(new MemoryStream(content, writable: false), ZipArchiveMode.Read));
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException)
        {
            throw new SellOutFormatException($"The workbook cannot be read: {exception.Message}");
        }
    }

    public IReadOnlyCollection<string> SheetNames => _sheetParts.Keys;

    /// <summary>
    /// The rows of a sheet, in order. Each row has its 1-based row number and its cells by 0-based
    /// column index. An empty cell is null.
    /// </summary>
    public IEnumerable<(int RowNumber, string?[] Cells)> ReadRows(string sheetName)
    {
        if (!_sheetParts.TryGetValue(sheetName, out var part))
        {
            throw new SellOutFormatException($"The workbook has no sheet '{sheetName}'.");
        }

        using var reader = OpenPart(part) ?? throw new SellOutFormatException($"The sheet '{sheetName}' is missing.");
        var rowNumber = 0;
        var cells = new List<string?>();
        reader.MoveToContent();
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.NamespaceURI == MainNamespace && reader.LocalName == "row")
            {
                if (rowNumber > 0)
                {
                    yield return (rowNumber, cells.ToArray());
                }

                rowNumber = int.TryParse(reader.GetAttribute("r"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                    ? number
                    : rowNumber + 1;
                cells.Clear();
                reader.Read();
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.NamespaceURI == MainNamespace && reader.LocalName == "c")
            {
                // ReadFrom reads the whole cell and moves the reader to the node after it.
                var cell = (XElement)XNode.ReadFrom(reader);
                var column = ColumnIndex((string?)cell.Attribute("r")) ?? cells.Count;
                while (cells.Count <= column)
                {
                    cells.Add(null);
                }

                cells[column] = CellValue(cell);
            }
            else
            {
                reader.Read();
            }
        }

        if (rowNumber > 0)
        {
            yield return (rowNumber, cells.ToArray());
        }
    }

    public void Dispose() => _zip.Dispose();

    private string? CellValue(XElement cell)
    {
        XNamespace main = MainNamespace;
        var type = (string?)cell.Attribute("t");
        string? value = type == "inlineStr"
            ? Text(cell.Element(main + "is"))
            : cell.Element(main + "v")?.Value;

        if (type == "s" && value is not null)
        {
            var index = int.Parse(value, CultureInfo.InvariantCulture);
            value = index >= 0 && index < _sharedStrings.Count ? _sharedStrings[index] : null;
        }

        return string.IsNullOrEmpty(value) ? null : value;
    }

    // The text of an <si> or <is> element: one <t>, or several rich-text runs, each with a <t>.
    private static string Text(XElement? element)
    {
        XNamespace main = MainNamespace;
        return element is null ? string.Empty : string.Concat(element.Descendants(main + "t").Select(text => text.Value));
    }

    private Dictionary<string, string> ReadSheetParts()
    {
        var relationships = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var rels = OpenPart("xl/_rels/workbook.xml.rels"))
        {
            while (rels is not null && rels.Read())
            {
                if (rels.NodeType == XmlNodeType.Element && rels.LocalName == "Relationship" && rels.NamespaceURI == PackageRelationshipNamespace)
                {
                    var target = rels.GetAttribute("Target") ?? string.Empty;
                    relationships[rels.GetAttribute("Id") ?? string.Empty] =
                        target.StartsWith('/') ? target.TrimStart('/') : $"xl/{target}";
                }
            }
        }

        var sheets = new Dictionary<string, string>(StringComparer.Ordinal);
        using var workbook = OpenPart("xl/workbook.xml") ?? throw new SellOutFormatException("The workbook part is missing.");
        while (workbook.Read())
        {
            if (workbook.NodeType == XmlNodeType.Element && workbook.LocalName == "sheet" && workbook.NamespaceURI == MainNamespace)
            {
                var name = workbook.GetAttribute("name");
                var id = workbook.GetAttribute("id", RelationshipNamespace);
                if (name is not null && id is not null && relationships.TryGetValue(id, out var part))
                {
                    sheets[name] = part;
                }
            }
        }

        return sheets;
    }

    private List<string> ReadSharedStrings()
    {
        var strings = new List<string>();
        using var reader = OpenPart("xl/sharedStrings.xml");
        if (reader is null)
        {
            return strings;
        }

        reader.MoveToContent();
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "si" && reader.NamespaceURI == MainNamespace)
            {
                strings.Add(Text((XElement)XNode.ReadFrom(reader)));
            }
            else
            {
                reader.Read();
            }
        }

        return strings;
    }

    private XmlReader? OpenPart(string name)
    {
        var entry = _zip.GetEntry(name);
        if (entry is null)
        {
            return null;
        }

        if (entry.Length > MaxPartBytes)
        {
            throw new SellOutFormatException($"The part {name} of the workbook is too large.");
        }

        return XmlReader.Create(entry.Open(), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = true,
            CloseInput = true,
        });
    }

    // The 0-based column of a cell reference: "A12" is 0, "Z3" is 25, "AB12" is 27.
    private static int? ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return null;
        }

        var index = 0;
        var letters = 0;
        foreach (var character in reference)
        {
            if (character is < 'A' or > 'Z')
            {
                break;
            }

            index = (index * 26) + (character - 'A' + 1);
            letters++;
        }

        return letters == 0 ? null : index - 1;
    }
}
