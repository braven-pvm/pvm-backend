using System.Text.Json;

namespace Pvm.Application.Documents;

/// <summary>
/// A general document (design 6.12): any paper staff want to keep. It is never matched, never goes
/// to the review list and never touches Acumatica. Claude reads a short title, a summary, the
/// printed date and a few search words, so the document can be found again.
/// </summary>
public sealed class GeneralDocumentType : IDocumentType
{
    // Structured outputs do not take maxLength or maxItems; GeneralDocumentFields enforces the limits.
    private const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "autoTitle": { "type": ["string", "null"] },
            "summary": { "type": ["string", "null"] },
            "documentDate": { "type": ["string", "null"], "format": "date" },
            "searchWords": { "type": "array", "items": { "type": "string" } }
          },
          "required": ["autoTitle", "summary", "documentDate", "searchWords"]
        }
        """;

    private static readonly JsonElement ParsedSchema = JsonDocument.Parse(Schema).RootElement.Clone();

    public string Code => DocumentTypeCodes.General;

    public string DisplayName => "General document";

    /// <summary>None: a general document is never attached in Acumatica.</summary>
    public string AcumaticaEntity => string.Empty;

    public JsonElement FieldSchema => ParsedSchema;

    public string ExtractionInstruction => """
        The attached PDF is a scanned paper that office staff want to keep and find again later.
        It can be any kind of paper: a letter, a statement, a quote, a certificate, a form, a note.

        Read the fields below so a person can find the paper by searching.
        - autoTitle: a short title of at most 80 characters that says what the paper is and who it is from,
          for example "Eskom electricity statement, October 2026".
        - summary: one sentence that says what the paper is about.
        - documentDate: the date printed on the paper (issue or letter date), as YYYY-MM-DD; null when there is none.
        - searchWords: up to 10 words or short phrases a person would search for, such as company names,
          reference or account numbers, people and products.

        Use null, or an empty list, when the paper cannot be read.
        """;
}

/// <summary>The codes of the known document types.</summary>
public static class DocumentTypeCodes
{
    public const string Pod = "POD";

    public const string General = "GENERAL";

    public static bool IsGeneral(string? code)
        => string.Equals(code, General, StringComparison.OrdinalIgnoreCase);
}
