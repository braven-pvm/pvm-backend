using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pvm.Application.Acumatica;
using Pvm.Application.Submissions;
using Pvm.Infrastructure.Operations;

namespace Pvm.Infrastructure.Acumatica;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAcumaticaInvoiceSource(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AcumaticaOptions>()
            .Bind(configuration.GetSection("Acumatica"));
        services.AddOptions<AcumaticaReconciliationOptions>()
            .Bind(configuration.GetSection(AcumaticaReconciliationOptions.SectionName))
            .Validate(
                options => options.ScheduleIntervalMinutes is >= 1 and <= 60,
                "Acumatica reconciliation interval must be between 1 and 60 minutes.")
            .Validate(
                options => options.OverlapMinutes is >= 1 and <= 1440,
                "Acumatica reconciliation overlap must be between 1 and 1440 minutes.")
            .Validate(
                options => options.DailyLookbackDays is >= 1 and <= 30,
                "Acumatica reconciliation daily lookback must be between 1 and 30 days.")
            .Validate(
                options => options.StaleAfterMinutes is >= 1 and <= 1440,
                "Acumatica reconciliation stale threshold must be between 1 and 1440 minutes.")
            .ValidateOnStart();
        services.AddHttpClient<IAcumaticaInvoiceClient, AcumaticaInvoiceClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(300);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            UseCookies = false
        });
        services.AddScoped<AcumaticaInvoiceCandidateRefreshService>();
        services.AddScoped<IInvoiceSourceVersionVerifier, AcumaticaInvoiceSourceVersionVerifier>();
        services.AddScoped<AcumaticaInvoiceReconciliationMessageHandler>();
        return services;
    }
}
