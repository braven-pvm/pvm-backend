using System.Text;
using Pvm.Application.Submissions;

namespace Pvm.Infrastructure.PayloadArchive;

public sealed class FileSystemPayloadArchive(
    string rootPath,
    string containerName) : IPayloadArchive
{
    private readonly string _rootPath = Path.GetFullPath(rootPath);

    public async Task<PayloadArchiveRecord> WriteAsync(
        PayloadArchiveWrite payload,
        CancellationToken cancellationToken)
    {
        PayloadArchiveContentGuard.EnsureSafe(payload.Content);
        var relativePath = NormalizePath(payload.Path);
        var targetPath = ResolvePath(relativePath);
        var bytes = PayloadArchiveHash.Bytes(payload.Content);
        var hash = PayloadArchiveHash.Sha256(bytes);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var temporaryPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
            try
            {
                File.Move(temporaryPath, targetPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(targetPath))
            {
                var existing = await File.ReadAllBytesAsync(targetPath, cancellationToken);
                if (!string.Equals(
                        PayloadArchiveHash.Sha256(existing),
                        hash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "An immutable payload archive location already contains different content.");
                }
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return new PayloadArchiveRecord(
            payload.Kind,
            $"{containerName}/{relativePath}",
            hash,
            payload.ContentType,
            bytes.LongLength,
            DateTimeOffset.UtcNow);
    }

    public async Task<string> ReadVerifiedAsync(
        PayloadArchiveRecord payload,
        CancellationToken cancellationToken)
    {
        var prefix = $"{containerName}/";
        if (!payload.Location.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Payload archive location does not belong to the configured container.");
        }

        var bytes = await File.ReadAllBytesAsync(
            ResolvePath(NormalizePath(payload.Location[prefix.Length..])),
            cancellationToken);
        if (!string.Equals(
                PayloadArchiveHash.Sha256(bytes),
                payload.Sha256Hash,
                StringComparison.OrdinalIgnoreCase)
            || bytes.LongLength != payload.ByteCount)
        {
            throw new InvalidOperationException("Payload archive hash or byte count verification failed.");
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private string ResolvePath(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, relativePath));
        var rootPrefix = _rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootPath
            : $"{_rootPath}{Path.DirectorySeparatorChar}";
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Payload archive path escapes its configured root.");
        }

        return fullPath;
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
