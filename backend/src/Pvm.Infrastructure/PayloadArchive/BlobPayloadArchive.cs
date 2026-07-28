using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Pvm.Application.Submissions;

namespace Pvm.Infrastructure.PayloadArchive;

public sealed class BlobPayloadArchive(
    BlobContainerClient container,
    string containerName) : IPayloadArchive
{
    public async Task<PayloadArchiveRecord> WriteAsync(
        PayloadArchiveWrite payload,
        CancellationToken cancellationToken)
    {
        PayloadArchiveContentGuard.EnsureSafe(payload.Content);
        var blobName = NormalizePath(payload.Path);
        var bytes = PayloadArchiveHash.Bytes(payload.Content);
        var hash = PayloadArchiveHash.Sha256(bytes);
        var blob = container.GetBlobClient(blobName);

        try
        {
            await blob.UploadAsync(
                BinaryData.FromBytes(bytes),
                new BlobUploadOptions
                {
                    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                    HttpHeaders = new BlobHttpHeaders { ContentType = payload.ContentType },
                    Metadata = new Dictionary<string, string>
                    {
                        ["sha256"] = hash,
                        ["kind"] = payload.Kind.ToString()
                    }
                },
                cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status is 409 or 412)
        {
            var existing = await blob.DownloadContentAsync(cancellationToken);
            var properties = await blob.GetPropertiesAsync(cancellationToken: cancellationToken);
            var existingBytes = existing.Value.Content.ToArray();
            if (!string.Equals(
                    PayloadArchiveHash.Sha256(existingBytes),
                    hash,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    properties.Value.ContentType,
                    payload.ContentType,
                    StringComparison.OrdinalIgnoreCase)
                || !properties.Value.Metadata.TryGetValue("kind", out var existingKind)
                || !string.Equals(
                    existingKind,
                    payload.Kind.ToString(),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "An immutable payload archive location already contains different content.");
            }
        }

        return new PayloadArchiveRecord(
            payload.Kind,
            $"{containerName}/{blobName}",
            hash,
            payload.ContentType,
            bytes.LongLength,
            DateTimeOffset.UtcNow);
    }

    public async Task<string> ReadVerifiedAsync(
        PayloadArchiveRecord payload,
        CancellationToken cancellationToken)
    {
        var blobName = LocationToBlobName(payload.Location);
        var download = await container.GetBlobClient(blobName).DownloadContentAsync(cancellationToken);
        var bytes = download.Value.Content.ToArray();
        var hash = PayloadArchiveHash.Sha256(bytes);
        if (!string.Equals(hash, payload.Sha256Hash, StringComparison.OrdinalIgnoreCase)
            || bytes.LongLength != payload.ByteCount)
        {
            throw new InvalidOperationException("Payload archive hash or byte count verification failed.");
        }

        return download.Value.Content.ToString();
    }

    private string LocationToBlobName(string location)
    {
        var prefix = $"{containerName}/";
        if (!location.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Payload archive location does not belong to the configured container.");
        }

        return NormalizePath(location[prefix.Length..]);
    }

    private static string NormalizePath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Split('/').Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("Payload archive path is invalid.", nameof(path));
        }

        return normalized;
    }
}
