using System.Text;
using Pvm.Infrastructure.Documents;

namespace Pvm.Infrastructure.Tests.Documents;

public sealed class FileSystemDocumentStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pvm-document-store-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Delete_RemovesTheFile()
    {
        var store = new FileSystemDocumentStore(_root, "documents");
        const string path = "documents/POD/2026/11/0b0f1d2e-0000-4000-8000-000000000001.pdf";
        await store.SaveAsync(path, new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7 scan")), default);

        await store.DeleteAsync(path, default);

        await Assert.ThrowsAnyAsync<IOException>(() => store.OpenReadAsync(path, default));
    }

    [Theory]
    [InlineData("documents/POD/2026/11/0b0f1d2e-0000-4000-8000-000000000002.pdf")]
    [InlineData("documents/POD/1999/01/0b0f1d2e-0000-4000-8000-000000000003.pdf")]
    public async Task Delete_MissingFile_IsNotAnError(string path)
    {
        var store = new FileSystemDocumentStore(_root, "documents");

        await store.DeleteAsync(path, default);
        await store.DeleteAsync(path, default);
    }

    [Fact]
    public async Task Delete_PathOutsideTheRoot_Refused()
    {
        var store = new FileSystemDocumentStore(_root, "documents");

        await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync("documents/../../escape.pdf", default));
    }
}
