using Pvm.Application.Submissions;
using Pvm.Infrastructure.PayloadArchive;

namespace Pvm.Infrastructure.Tests.PayloadArchive;

public sealed class FileSystemPayloadArchiveTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"pvm-file-payload-tests-{Guid.NewGuid():N}");
    private readonly FileSystemPayloadArchive _archive;

    public FileSystemPayloadArchiveTests()
    {
        _archive = new FileSystemPayloadArchive(_root, "payloads");
    }

    [Fact]
    public async Task Write_is_immutable_idempotent_and_hash_verified()
    {
        var request = new PayloadArchiveWrite(
            PayloadArchiveKind.ShopriteRequest,
            "shoprite/invoices/2026/07/operation-1/request.xml",
            "application/xml",
            "<invoice>safe</invoice>");

        var first = await _archive.WriteAsync(request, CancellationToken.None);
        var redelivery = await _archive.WriteAsync(request, CancellationToken.None);

        Assert.Equal(first.Location, redelivery.Location);
        Assert.Equal(first.Sha256Hash, redelivery.Sha256Hash);
        Assert.Equal(request.Content, await _archive.ReadVerifiedAsync(first, CancellationToken.None));

        var changed = request with { Content = "<invoice>changed</invoice>" };
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _archive.WriteAsync(changed, CancellationToken.None));
    }

    [Theory]
    [InlineData("""{"password":"not-archivable"}""")]
    [InlineData("Authorization: Bearer not-archivable")]
    [InlineData("https://user:not-archivable@example.invalid/path")]
    [InlineData("https://example.invalid/path?access_token=not-archivable")]
    public async Task Credential_shaped_content_is_rejected(string content)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _archive.WriteAsync(
                new PayloadArchiveWrite(
                    PayloadArchiveKind.ShopriteResponse,
                    $"shoprite/invoices/2026/07/{Guid.NewGuid():D}/response.txt",
                    "text/plain",
                    content),
                CancellationToken.None));

        Assert.DoesNotContain("not-archivable", exception.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
