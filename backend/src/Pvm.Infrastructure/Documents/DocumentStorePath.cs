using System.Security.Cryptography;

namespace Pvm.Infrastructure.Documents;

internal static class DocumentStorePath
{
    /// <summary>Turns <c>{container}/{path}</c> into the path inside the container, and refuses a path that leaves it.</summary>
    public static string WithinContainer(string blobPath, string containerName)
    {
        var normalized = blobPath.Replace('\\', '/').Trim('/');
        var prefix = $"{containerName}/";
        if (!normalized.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("Document path does not belong to the configured container.", nameof(blobPath));
        }

        var relative = normalized[prefix.Length..];
        if (string.IsNullOrWhiteSpace(relative)
            || relative.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new ArgumentException("Document path is invalid.", nameof(blobPath));
        }

        return relative;
    }

    public static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    public static string Sha256(ReadOnlySpan<byte> content)
        => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}
