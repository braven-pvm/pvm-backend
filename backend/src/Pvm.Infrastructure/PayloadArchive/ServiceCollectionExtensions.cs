using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pvm.Application.Submissions;

namespace Pvm.Infrastructure.PayloadArchive;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPayloadArchive(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration
            .GetSection(PayloadArchiveOptions.SectionName)
            .Get<PayloadArchiveOptions>() ?? new PayloadArchiveOptions();

        if (string.Equals(options.Provider, "AzureBlob", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(options.ServiceUri, UriKind.Absolute, out var serviceUri)
                || serviceUri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(serviceUri.UserInfo)
                || !string.IsNullOrEmpty(serviceUri.Query))
            {
                throw new InvalidOperationException(
                    "PayloadArchive:ServiceUri must be a credential-free HTTPS Blob service URI.");
            }

            services.AddSingleton<IPayloadArchive>(_ =>
            {
                var credential = new DefaultAzureCredential();
                var service = new BlobServiceClient(serviceUri, credential);
                return new BlobPayloadArchive(
                    service.GetBlobContainerClient(options.ContainerName),
                    options.ContainerName);
            });
            return services;
        }

        if (!string.Equals(options.Provider, "FileSystem", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported payload archive provider '{options.Provider}'.");
        }

        services.AddSingleton<IPayloadArchive>(
            new FileSystemPayloadArchive(options.FileSystemRoot, options.ContainerName));
        return services;
    }
}
