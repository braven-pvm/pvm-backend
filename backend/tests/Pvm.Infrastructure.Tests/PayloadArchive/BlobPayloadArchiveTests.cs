using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Pvm.Application.Submissions;
using Pvm.Infrastructure.PayloadArchive;
using Testcontainers.Azurite;

namespace Pvm.Infrastructure.Tests.PayloadArchive;

public sealed class BlobPayloadArchiveTests : IAsyncLifetime
{
    private AzuriteContainer? _azurite;
    private BlobContainerClient _container = null!;
    private BlobPayloadArchive _archive = null!;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "PVM_TEST_AZURITE_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.35.0")
                .Build();
            await _azurite.StartAsync();
            connectionString = _azurite.GetConnectionString();
        }

        _container = new BlobContainerClient(
            connectionString,
            "payloads",
            new BlobClientOptions(BlobClientOptions.ServiceVersion.V2023_11_03));
        await _container.CreateIfNotExistsAsync();
        _archive = new BlobPayloadArchive(_container, "payloads");
    }

    public Task DisposeAsync()
        => _azurite is null
            ? Task.CompletedTask
            : _azurite.DisposeAsync().AsTask();

    [Fact]
    public async Task Write_is_immutable_idempotent_and_hash_verified()
    {
        var path = $"shoprite/invoices/2026/07/{Guid.NewGuid():D}/request.xml";
        var request = new PayloadArchiveWrite(
            PayloadArchiveKind.ShopriteRequest,
            path,
            "application/xml",
            "<invoice>safe</invoice>");

        var first = await _archive.WriteAsync(request, CancellationToken.None);
        var redelivery = await _archive.WriteAsync(request, CancellationToken.None);

        Assert.Equal(first.Location, redelivery.Location);
        Assert.Equal(first.Sha256Hash, redelivery.Sha256Hash);
        Assert.Equal(64, first.Sha256Hash.Length);
        Assert.Equal(request.Content, await _archive.ReadVerifiedAsync(first, CancellationToken.None));
        Assert.Single(
            _container.GetBlobs(
                BlobTraits.None,
                BlobStates.None,
                path,
                CancellationToken.None));
    }

    [Fact]
    public async Task Existing_location_with_different_content_is_rejected()
    {
        var path = $"shoprite/invoices/2026/07/{Guid.NewGuid():D}/request.xml";
        await _archive.WriteAsync(
            new PayloadArchiveWrite(
                PayloadArchiveKind.ShopriteRequest,
                path,
                "application/xml",
                "<invoice>first</invoice>"),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _archive.WriteAsync(
                new PayloadArchiveWrite(
                    PayloadArchiveKind.ShopriteRequest,
                    path,
                    "application/xml",
                    "<invoice>changed</invoice>"),
                CancellationToken.None));

        Assert.Contains("different content", exception.Message);
    }

    [Theory]
    [InlineData("""{"password":"not-archivable"}""")]
    [InlineData("Authorization: Bearer not-archivable")]
    [InlineData("https://user:not-archivable@example.invalid/path")]
    [InlineData("https://example.invalid/path?access_token=not-archivable")]
    public async Task Credential_shaped_content_is_rejected(string content)
    {
        var path = $"shoprite/invoices/2026/07/{Guid.NewGuid():D}/response.txt";
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _archive.WriteAsync(
                new PayloadArchiveWrite(
                    PayloadArchiveKind.ShopriteResponse,
                    path,
                    "text/plain",
                    content),
                CancellationToken.None));

        Assert.DoesNotContain("not-archivable", exception.Message);
        Assert.False(await _container.GetBlobClient(path).ExistsAsync());
    }

    [Fact]
    public async Task Read_rejects_content_that_no_longer_matches_persisted_hash()
    {
        var path = $"shoprite/invoices/2026/07/{Guid.NewGuid():D}/request.xml";
        var record = await _archive.WriteAsync(
            new PayloadArchiveWrite(
                PayloadArchiveKind.ShopriteRequest,
                path,
                "application/xml",
                "<invoice>original</invoice>"),
            CancellationToken.None);
        await _container
            .GetBlobClient(path)
            .UploadAsync(BinaryData.FromString("<invoice>tampered</invoice>"), overwrite: true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _archive.ReadVerifiedAsync(record, CancellationToken.None));
    }
}
