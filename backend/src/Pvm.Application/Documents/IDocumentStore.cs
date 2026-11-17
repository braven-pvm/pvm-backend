namespace Pvm.Application.Documents;

/// <summary>Keeps the original PDF of each captured document (design 6.6).</summary>
public interface IDocumentStore
{
    /// <summary>Saves the PDF at <paramref name="blobPath"/> (<c>{container}/{documentType}/{yyyy}/{MM}/{documentId}.pdf</c>). A path is written once: the same bytes again succeed, other bytes are refused.</summary>
    Task<StoredDocument> SaveAsync(string blobPath, Stream pdf, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string blobPath, CancellationToken cancellationToken);
}

/// <param name="Sha256">Lower-case hex SHA-256 of the stored bytes.</param>
public sealed record StoredDocument(string Sha256, long SizeBytes);
