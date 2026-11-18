using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pvm.Application.Documents;

namespace Pvm.Infrastructure.Documents;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="DocumentCaptureOptions"/>, the known document types (as
    /// <c>IEnumerable&lt;IDocumentType&gt;</c>), the Claude extractor (options from section
    /// <c>Anthropic</c>; the API key is checked on first use), and the document store that
    /// <c>DocumentCapture:Provider</c> selects.
    /// </summary>
    public static IServiceCollection AddDocumentCapture(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(DocumentCaptureOptions.SectionName);
        var options = section.Get<DocumentCaptureOptions>() ?? new DocumentCaptureOptions();
        services.Configure<DocumentCaptureOptions>(section);
        services.AddSingleton<IDocumentType, ProofOfDeliveryDocumentType>();
        services.Configure<AnthropicOptions>(configuration.GetSection(AnthropicOptions.SectionName));
        services.AddSingleton<IDocumentExtractor>(sp =>
            new ClaudeDocumentExtractor(
                sp.GetRequiredService<IOptions<AnthropicOptions>>(),
                sp.GetRequiredService<ILogger<ClaudeDocumentExtractor>>()));

        if (string.Equals(options.Provider, "AzureBlob", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(options.ServiceUri, UriKind.Absolute, out var serviceUri)
                || serviceUri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(serviceUri.UserInfo)
                || !string.IsNullOrEmpty(serviceUri.Query))
            {
                throw new InvalidOperationException(
                    "DocumentCapture:ServiceUri must be a credential-free HTTPS Blob service URI.");
            }

            services.AddSingleton<IDocumentStore>(_ =>
            {
                var service = new BlobServiceClient(serviceUri, new DefaultAzureCredential());
                return new BlobDocumentStore(
                    service.GetBlobContainerClient(options.ContainerName),
                    options.ContainerName);
            });
            return services;
        }

        if (!string.Equals(options.Provider, "FileSystem", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported document store provider '{options.Provider}'.");
        }

        services.AddSingleton<IDocumentStore>(
            new FileSystemDocumentStore(options.FileSystemRoot, options.ContainerName));
        return services;
    }
}
