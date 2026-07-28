using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pvm.Application.Submissions;
using Pvm.Infrastructure.PayloadArchive;

namespace Pvm.Infrastructure.Tests.PayloadArchive;

public sealed class PayloadArchiveRegistrationTests
{
    [Fact]
    public void Azure_blob_provider_resolves_managed_identity_archive()
    {
        var configuration = Configuration(
            ("PayloadArchive:Provider", "AzureBlob"),
            ("PayloadArchive:ContainerName", "payloads"),
            ("PayloadArchive:ServiceUri", "https://storage.example.invalid"));
        var services = new ServiceCollection();

        services.AddPayloadArchive(configuration);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<BlobPayloadArchive>(provider.GetRequiredService<IPayloadArchive>());
    }

    [Theory]
    [InlineData("http://storage.example.invalid")]
    [InlineData("https://user:secret@storage.example.invalid")]
    [InlineData("https://storage.example.invalid?sig=not-allowed")]
    public void Azure_blob_provider_rejects_non_https_or_credential_bearing_service_uri(
        string serviceUri)
    {
        var configuration = Configuration(
            ("PayloadArchive:Provider", "AzureBlob"),
            ("PayloadArchive:ServiceUri", serviceUri));
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddPayloadArchive(configuration));

        Assert.DoesNotContain("secret", exception.Message);
        Assert.DoesNotContain("sig=", exception.Message);
    }

    private static IConfiguration Configuration(
        params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(
                values.ToDictionary(
                    item => item.Key,
                    item => (string?)item.Value))
            .Build();
}
