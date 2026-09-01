using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pvm.Application.Banking;
using Pvm.Application.Investec;
using Pvm.Infrastructure.Acumatica;

namespace Pvm.Infrastructure.Investec;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Investec → Acumatica bank feed: the Investec transaction client, the
    /// statement mapper, and the Acumatica bank-statement write client. Assumes
    /// <c>AddAcumaticaInvoiceSource</c> has bound <see cref="AcumaticaOptions"/> (shared instance/credentials).
    /// </summary>
    public static IServiceCollection AddInvestecBankFeed(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<InvestecOptions>()
            .Bind(configuration.GetSection(InvestecOptions.SectionName))
            .Validate(
                options => options.MaxPages is >= 1 and <= 10_000,
                "Investec MaxPages must be between 1 and 10000.");
        services.AddOptions<AcumaticaBankFeedOptions>()
            .Bind(configuration.GetSection(AcumaticaBankFeedOptions.SectionName));

        services.AddHttpClient<IInvestecTransactionClient, InvestecTransactionClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(300);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PVM-BankFeed/1.0");
        });
        services.AddHttpClient<IAcumaticaBankStatementClient, AcumaticaBankStatementClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(300);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PVM-BankFeed/1.0");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            UseCookies = false
        });

        services.AddSingleton<InvestecBankStatementMapper>();
        services.AddScoped<InvestecBankFeedRefreshService>();
        return services;
    }
}
