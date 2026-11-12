using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pvm.Application.Reporting;
using Pvm.Domain.Reporting;

namespace Pvm.Infrastructure.Reporting;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Acumatica sell-in load. Assumes <c>AddAcumaticaInvoiceSource</c> has bound the
    /// shared Acumatica options. Register the payload archive too, to keep raw responses.
    /// </summary>
    public static IServiceCollection AddSellInSync(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SellInSyncOptions>()
            .Bind(configuration.GetSection(SellInSyncOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.InquiryName), "SellIn InquiryName is required.")
            .Validate(o => o.PeriodsBack is >= 0 and <= 120, "SellIn PeriodsBack must be 0 to 120.")
            .Validate(o => o.PeriodsAhead is >= 0 and <= 2, "SellIn PeriodsAhead must be 0 to 2.")
            .Validate(o => string.IsNullOrWhiteSpace(o.FirstPeriod) || PostingPeriod.TryParse(o.FirstPeriod, out _),
                "SellIn FirstPeriod must be a posting period such as 012019.")
            .Validate(o => o.ReadTimeoutSeconds is >= 10 and <= 1800, "SellIn ReadTimeoutSeconds must be 10 to 1800.");

        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient<ISellInSource, AcumaticaSellInSource>(client =>
        {
            // The source sets its own timeout for each period read.
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PVM-SellIn/1.0");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false });

        services.AddScoped<SellInSyncService>();
        return services;
    }
}
