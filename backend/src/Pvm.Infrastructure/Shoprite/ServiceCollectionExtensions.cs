using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pvm.Application.Submissions;

namespace Pvm.Infrastructure.Shoprite;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddShopriteClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ShopriteOptions>()
            .Bind(configuration.GetSection("Shoprite"))
            .Validate(
                options => IsAbsoluteHttpsUri(options.BaseUrl),
                "Shoprite:BaseUrl must be a non-empty absolute HTTPS URI.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Username),
                "Shoprite:Username is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Password),
                "Shoprite:Password is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ContractId),
                "Shoprite:ContractId is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.UiUser),
                "Shoprite:UiUser is required.")
            .ValidateOnStart();

        services.AddHttpClient<IShopriteInvoiceClient, ShopriteInvoiceClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<ShopriteOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(300);
        });

        return services;
    }

    private static bool IsAbsoluteHttpsUri(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps;
    }
}
