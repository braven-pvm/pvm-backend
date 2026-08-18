using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pvm.Application.Banking;
using Pvm.Infrastructure.Acumatica;

namespace Pvm.Infrastructure.Nedbank;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Nedbank manual statement import: the OFX parser, the Acumatica
    /// bank-statement write client (the shared <c>PVMBankFeed</c> endpoint), and the import
    /// service. Assumes <c>AddAcumaticaInvoiceSource</c> has bound <see cref="AcumaticaOptions"/>
    /// (shared instance/credentials).
    /// </summary>
    public static IServiceCollection AddNedbankStatementImport(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<NedbankOptions>()
            .Bind(configuration.GetSection(NedbankOptions.SectionName));
        services.AddOptions<AcumaticaBankFeedOptions>()
            .Bind(configuration.GetSection(AcumaticaBankFeedOptions.SectionName));

        services.AddHttpClient<IAcumaticaBankStatementClient, AcumaticaBankStatementClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(300);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            UseCookies = false
        });

        services.AddSingleton<NedbankOfxParser>();
        services.AddScoped<NedbankStatementImportService>();
        return services;
    }
}
