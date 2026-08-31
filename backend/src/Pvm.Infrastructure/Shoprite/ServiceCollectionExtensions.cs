using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pvm.Application.Shoprite;
using Pvm.Application.Submissions;
using Pvm.Infrastructure.Operations;

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
                options => !options.UseLayer7Headers || !string.IsNullOrWhiteSpace(options.ContractId),
                "Shoprite:ContractId is required when Shoprite:UseLayer7Headers is enabled.")
            .ValidateOnStart();

        services.TryAddTransient<ShopriteLayer7Handler>();

        services.AddHttpClient<IShopriteInvoiceClient, ShopriteInvoiceClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<ShopriteOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl!.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(300);
        }).AddHttpMessageHandler<ShopriteLayer7Handler>();

        return services;
    }

    public static IServiceCollection AddShopritePurchaseOrderClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ShopriteOptions>(configuration.GetSection("Shoprite"));
        services.TryAddTransient<ShopriteLayer7Handler>();
        services.AddHttpClient<IShopritePurchaseOrderClient, ShopritePurchaseOrderClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(300);
        }).AddHttpMessageHandler<ShopriteLayer7Handler>();
        services.AddScoped<ShopriteOrderAcknowledgementService>();
        services.AddScoped<ShopritePurchaseOrderRefreshService>();
        services.AddScoped<ShopriteInvoiceCandidateRevalidationService>();
        services.AddScoped<ShopritePurchaseOrderRefreshMessageHandler>();
        services.AddOptions<ShopritePurchaseOrderRefreshOptions>()
            .Bind(configuration.GetSection(ShopritePurchaseOrderRefreshOptions.SectionName))
            .Validate(options => options.ScheduleIntervalMinutes >= 1,
                "ShopritePoRefresh:ScheduleIntervalMinutes must be positive.")
            .Validate(options => options.StaleAfterMinutes >= options.ScheduleIntervalMinutes,
                "ShopritePoRefresh:StaleAfterMinutes must be at least the schedule interval.")
            .ValidateOnStart();

        return services;
    }

    private static bool IsAbsoluteHttpsUri(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps;
    }
}
