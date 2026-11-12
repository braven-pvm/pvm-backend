using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pvm.Application.Stock;

namespace Pvm.Infrastructure.Stock;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Acumatica → WooCommerce stock sync. Assumes <c>AddAcumaticaInvoiceSource</c>
    /// has bound the shared Acumatica options.
    /// </summary>
    public static IServiceCollection AddStockSync(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StockSyncOptions>()
            .Bind(configuration.GetSection(StockSyncOptions.SectionName))
            .Validate(o => o.MinimumPublishedQuantity >= 0, "StockSync MinimumPublishedQuantity must not be negative.")
            .Validate(o => o.OpenOrderLookbackDays is >= 1 and <= 365, "StockSync OpenOrderLookbackDays must be 1 to 365.")
            .Validate(o => o.MaximumItemsZeroedPerRun >= 0, "StockSync MaximumItemsZeroedPerRun must not be negative.");
        services.AddOptions<WooCommerceOptions>()
            .Bind(configuration.GetSection(WooCommerceOptions.SectionName))
            .Validate(o => o.PageSize is >= 1 and <= 100, "WooCommerce PageSize must be 1 to 100.");

        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient<IWooCommerceStockClient, WooCommerceStockClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(120);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PVM-StockSync/1.0");
        });
        services.AddHttpClient<IAcumaticaStockReader, AcumaticaStockReader>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(300);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PVM-StockSync/1.0");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false });

        services.AddScoped<StockSyncService>();
        services.AddScoped<StockSyncRunRecorder>();
        return services;
    }

    /// <summary>Registers only what reading recorded stock-sync runs needs, for the API.</summary>
    public static IServiceCollection AddStockSyncRunReader(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<StockSyncRunRecorder>();
        return services;
    }
}
