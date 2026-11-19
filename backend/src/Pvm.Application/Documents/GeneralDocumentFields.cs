using System.Globalization;
using System.Text.Json;

namespace Pvm.Application.Documents;

/// <summary>What Claude read from a general document, cleaned and cut to the stored limits.</summary>
public sealed record GeneralExtraction(
    string? AutoTitle,
    string? Summary,
    DateOnly? DocumentDate,
    IReadOnlyList<string> SearchWords)
{
    /// <summary>True when nothing usable was read.</summary>
    public bool IsEmpty => AutoTitle is null && Summary is null && DocumentDate is null && SearchWords.Count == 0;
}

/// <summary>
/// The limits of a general document's fields (design 6.12), the reading of Claude's fields, and
/// the lower-case search text that the General scans search runs on.
/// </summary>
public static class GeneralDocumentFields
{
    /// <summary>The title typed on the phone.</summary>
    public const int MaxTitleLength = 120;

    /// <summary>The note typed on the phone, for general documents and POD.</summary>
    public const int MaxNoteLength = 1000;

    public const int MaxAutoTitleLength = 80;

    public const int MaxSummaryLength = 500;

    public const int MaxSearchWords = 10;

    public const int MaxSearchWordLength = 60;

    /// <summary>
    /// Reads the fields of <see cref="GeneralDocumentType.FieldSchema"/>. Property names are matched
    /// without regard to case. A field of the wrong kind counts as missing; text is trimmed and cut
    /// to its limit; a date that is not YYYY-MM-DD is dropped; search words are trimmed, de-duplicated
    /// (ignoring case) and at most <see cref="MaxSearchWords"/>.
    /// </summary>
    public static GeneralExtraction Read(JsonElement fields)
    {
        if (fields.ValueKind != JsonValueKind.Object)
        {
            return new GeneralExtraction(null, null, null, []);
        }

        var autoTitle = Text(Property(fields, "autoTitle"), MaxAutoTitleLength);
        var summary = Text(Property(fields, "summary"), MaxSummaryLength);
        DateOnly? date = null;
        if (Text(Property(fields, "documentDate"), 10) is { } rawDate
            && DateOnly.TryParseExact(rawDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            date = parsed;
        }

        var words = new List<string>();
        if (Property(fields, "searchWords") is { ValueKind: JsonValueKind.Array } array)
        {
            foreach (var item in array.EnumerateArray())
            {
                if (Text(item, MaxSearchWordLength) is { } word
                    && !words.Contains(word, StringComparer.OrdinalIgnoreCase))
                {
                    words.Add(word);
                    if (words.Count == MaxSearchWords)
                    {
                        break;
                    }
                }
            }
        }

        return new GeneralExtraction(autoTitle, summary, date, words);
    }

    /// <summary>
    /// The text the General scans search matches: the typed title and note, the automatic title,
    /// the summary and the search words, in lower case, one per line. Null when all are empty.
    /// </summary>
    public static string? SearchText(
        string? title,
        string? note,
        string? autoTitle = null,
        string? summary = null,
        IEnumerable<string>? searchWords = null)
    {
        var parts = new[] { title, note, autoTitle, summary }
            .Concat(searchWords ?? [])
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim().ToLowerInvariant())
            .ToList();
        return parts.Count == 0 ? null : string.Join('\n', parts);
    }

    /// <summary>A typed field: trimmed, null when blank.</summary>
    public static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static JsonElement? Property(JsonElement fields, string name)
    {
        foreach (var property in fields.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string? Text(JsonElement? value, int maxLength)
    {
        if (value is not { ValueKind: JsonValueKind.String } element)
        {
            return null;
        }

        var text = string.Join(' ', (element.GetString() ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length == 0)
        {
            return null;
        }

        return text.Length <= maxLength ? text : text[..maxLength].TrimEnd();
    }
}
