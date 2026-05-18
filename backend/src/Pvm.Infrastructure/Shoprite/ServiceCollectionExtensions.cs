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
        services.Configure<ShopriteOptions>(configuration.GetSection("Shoprite"));
        services.AddHttpClient<IShopriteInvoiceClient, ShopriteInvoiceClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<ShopriteOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(300);
        });

        return services;
    }
}
