using Pvm.Application.Documents;

namespace Pvm.Infrastructure.Persistence.Entities;

/// <summary>One scanned document: the PDF in blob storage, its state, and what it was matched to (design 6.6, 6.7).</summary>
public sealed class CapturedDocumentEntity
{
    public Guid Id { get; set; }

    /// <summary>The ID the phone app made for the upload; a second upload with the same ID is the same document.</summary>
    public Guid ClientDocumentId { get; set; }

    /// <summary>The <see cref="IDocumentType.Code"/>, such as POD.</summary>
    public required string DocumentType { get; set; }

    public DocumentState State { get; set; }

    public ReviewReason? ReviewReason { get; set; }

    /// <summary><c>documents/{documentType}/{yyyy}/{MM}/{documentId}.pdf</c>. Never the invoice number.</summary>
    public required string BlobPath { get; set; }

    public int PageCount { get; set; }

    public long SizeBytes { get; set; }

    public required string Sha256 { get; set; }

    public string? ExtractionJson { get; set; }

    public string? MatchedReference { get; set; }

    public string? MatchedCustomerName { get; set; }

    /// <summary>Who matched the document: the worker, or the person who resolved the review.</summary>
    public string? MatchedBy { get; set; }

    /// <summary>True while the Acumatica attachment waits, for example in shadow mode.</summary>
    public bool AcumaticaAttachPending { get; set; }

    public DateTimeOffset? AcumaticaAttachedAt { get; set; }

    public required string UploadedBy { get; set; }

    public DateTimeOffset UploadedAt { get; set; }

    public string? ResolvedBy { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public string? ReviewNote { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
