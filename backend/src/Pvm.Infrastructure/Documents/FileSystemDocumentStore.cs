using Pvm.Application.Documents;

namespace Pvm.Infrastructure.Documents;

/// <summary>Keeps documents on the local disk, for development and tests.</summary>
public sealed class FileSystemDocumentStore(
    string rootPath,
    string containerName) : IDocumentStore
{
    private readonly string _rootPath = Path.GetFullPath(rootPath);

    public async Task<StoredDocument> SaveAsync(
        string blobPath,
        Stream pdf,
        CancellationToken cancellationToken)
    {
        var targetPath = ResolvePath(blobPath);
        var bytes = await DocumentStorePath.ReadAllAsync(pdf, cancellationToken);
        var hash = DocumentStorePath.Sha256(bytes);
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
                if (!string.Equals(DocumentStorePath.Sha256(existing), hash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "An immutable document location already contains different content.");
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

        return new StoredDocument(hash, bytes.LongLength);
    }

    public Task<Stream> OpenReadAsync(string blobPath, CancellationToken cancellationToken)
        => Task.FromResult<Stream>(File.OpenRead(ResolvePath(blobPath)));

    private string ResolvePath(string blobPath)
    {
        var relativePath = DocumentStorePath.WithinContainer(blobPath, containerName);
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, relativePath));
        var rootPrefix = _rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootPath
            : $"{_rootPath}{Path.DirectorySeparatorChar}";
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Document path escapes its configured root.");
        }

        return fullPath;
    }
}
