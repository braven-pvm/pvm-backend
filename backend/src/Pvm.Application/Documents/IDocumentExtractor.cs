using System.Text.Json;

namespace Pvm.Application.Documents;

/// <summary>Reads the fields of a document type from a scanned PDF (design 6.4).</summary>
public interface IDocumentExtractor
{
    /// <summary>
    /// Returns <see cref="Extracted"/> with fields that match <see cref="IDocumentType.FieldSchema"/>,
    /// or <see cref="Unreadable"/> when the model declined or gave no usable result.
    /// Transport failures (network, rate limit, server errors) throw, so the message is retried.
    /// </summary>
    Task<ExtractionResult> ExtractAsync(IDocumentType type, Stream pdf, CancellationToken cancellationToken);
}

/// <summary>The outcome of one extraction.</summary>
public abstract record ExtractionResult;

/// <summary>The fields as a JSON object that has the top-level shape of the type's field schema.</summary>
public sealed record Extracted(JsonElement Fields) : ExtractionResult;

/// <summary>No usable fields: <c>refusal</c>, <c>max_tokens</c>, <c>empty</c>, <c>malformed</c>, or <c>schema</c>.</summary>
public sealed record Unreadable(string Reason) : ExtractionResult;
