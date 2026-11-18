using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Pvm.Application.Documents;

namespace Pvm.Infrastructure.Documents;

public sealed class BlobDocumentStore(
    BlobContainerClient container,
    string containerName) : IDocumentStore
{
    public async Task<StoredDocument> SaveAsync(
        string blobPath,
        Stream pdf,
        CancellationToken cancellationToken)
    {
        var blob = container.GetBlobClient(DocumentStorePath.WithinContainer(blobPath, containerName));
        var bytes = await DocumentStorePath.ReadAllAsync(pdf, cancellationToken);
        var hash = DocumentStorePath.Sha256(bytes);

        try
        {
            await blob.UploadAsync(
                BinaryData.FromBytes(bytes),
                new BlobUploadOptions
                {
                    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/pdf" },
                    Metadata = new Dictionary<string, string> { ["sha256"] = hash }
                },
                cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status is 409 or 412)
        {
            var existing = await blob.DownloadContentAsync(cancellationToken);
            if (!string.Equals(
                    DocumentStorePath.Sha256(existing.Value.Content.ToMemory().Span),
                    hash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "An immutable document location already contains different content.");
            }
        }

        return new StoredDocument(hash, bytes.LongLength);
    }

    public async Task<Stream> OpenReadAsync(string blobPath, CancellationToken cancellationToken)
        => await container
            .GetBlobClient(DocumentStorePath.WithinContainer(blobPath, containerName))
            .OpenReadAsync(cancellationToken: cancellationToken);
}
