using System.Security.Cryptography;
using System.Text;
using Azure.Storage.Blobs;
using Pvm.Infrastructure.Documents;
using Testcontainers.Azurite;

namespace Pvm.Infrastructure.Tests.Documents;

public sealed class BlobDocumentStoreTests : IAsyncLifetime
{
    private AzuriteContainer? _azurite;
    private BlobContainerClient _container = null!;
    private BlobDocumentStore _store = null!;

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
            "documents",
            new BlobClientOptions(BlobClientOptions.ServiceVersion.V2023_11_03));
        await _container.CreateIfNotExistsAsync();
        _store = new BlobDocumentStore(_container, "documents");
    }

    public Task DisposeAsync()
        => _azurite is null
            ? Task.CompletedTask
            : _azurite.DisposeAsync().AsTask();

    [Fact]
    public async Task BlobDocumentStore_Save_ThenOpenRead_ReturnsSameBytes()
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\n% proof of delivery\n%%EOF\n");
        var path = $"documents/POD/2026/11/{Guid.NewGuid():D}.pdf";

        var stored = await _store.SaveAsync(path, new MemoryStream(bytes), CancellationToken.None);

        Assert.Equal(bytes.LongLength, stored.SizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), stored.Sha256);
        await using var read = await _store.OpenReadAsync(path, CancellationToken.None);
        using var copy = new MemoryStream();
        await read.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
        Assert.True(await _container.GetBlobClient(path["documents/".Length..]).ExistsAsync());
    }

    [Fact]
    public async Task BlobDocumentStore_Save_ExistingPathWithDifferentBytes_IsRejected()
    {
        var path = $"documents/POD/2026/11/{Guid.NewGuid():D}.pdf";
        await _store.SaveAsync(path, new MemoryStream(Encoding.ASCII.GetBytes("%PDF-first")), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _store.SaveAsync(path, new MemoryStream(Encoding.ASCII.GetBytes("%PDF-second")), CancellationToken.None));
    }
}
