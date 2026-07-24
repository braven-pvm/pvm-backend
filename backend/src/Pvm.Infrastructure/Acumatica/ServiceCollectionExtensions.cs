using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pvm.Application.Acumatica;

namespace Pvm.Infrastructure.Acumatica;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAcumaticaInvoiceSource(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AcumaticaOptions>()
            .Bind(configuration.GetSection("Acumatica"));
        services.AddHttpClient<IAcumaticaInvoiceClient, AcumaticaInvoiceClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(300);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            UseCookies = false
        });
        services.AddScoped<AcumaticaInvoiceCandidateRefreshService>();
        return services;
    }
}
