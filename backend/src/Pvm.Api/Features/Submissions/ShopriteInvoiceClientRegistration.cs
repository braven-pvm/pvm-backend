using Pvm.Application.Submissions;
using Pvm.Infrastructure.Shoprite;

namespace Pvm.Api.Features.Submissions;

public static class ShopriteInvoiceClientRegistration
{
    public static IServiceCollection AddConfiguredShopriteInvoiceClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var mode = configuration["Shoprite:InvoiceSubmissionMode"];
        if (string.Equals(mode, "Real", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "RealQa", StringComparison.OrdinalIgnoreCase))
        {
            return services.AddShopriteClient(configuration);
        }

        services.AddSingleton<IShopriteInvoiceClient, LocalShopriteInvoiceClient>();
        return services;
    }
}
