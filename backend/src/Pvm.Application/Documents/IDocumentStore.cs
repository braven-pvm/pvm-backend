namespace Pvm.Application.Documents;

/// <summary>Keeps the original PDF of each captured document (design 6.6).</summary>
public interface IDocumentStore
{
    /// <summary>Saves the PDF at <paramref name="blobPath"/> (<c>{container}/{documentType}/{yyyy}/{MM}/{documentId}.pdf</c>). A path is written once: the same bytes again succeed, other bytes are refused.</summary>
    Task<StoredDocument> SaveAsync(string blobPath, Stream pdf, CancellationToken cancellationToken);

    /// <summary>
    /// Saves another file next to a document, such as the phone's thumbnail of the first page
    /// (<c>image/jpeg</c>), with the same write-once rule. A store without content types saves it like a PDF.
    /// </summary>
    Task<StoredDocument> SaveAsync(string blobPath, Stream content, string contentType, CancellationToken cancellationToken)
        => SaveAsync(blobPath, content, cancellationToken);

    Task<Stream> OpenReadAsync(string blobPath, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the PDF for good (design 6.11). Deleting a PDF that is not there is not an error,
    /// so a retry after a partial failure succeeds.
    /// </summary>
    Task DeleteAsync(string blobPath, CancellationToken cancellationToken);
}

/// <param name="Sha256">Lower-case hex SHA-256 of the stored bytes.</param>
public sealed record StoredDocument(string Sha256, long SizeBytes);
