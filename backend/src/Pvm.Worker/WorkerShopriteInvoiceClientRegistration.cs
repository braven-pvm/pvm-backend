using Pvm.Application.Submissions;
using Pvm.Infrastructure.Shoprite;

namespace Pvm.Worker;

public static class WorkerShopriteInvoiceClientRegistration
{
    public static IServiceCollection AddWorkerShopriteInvoiceClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (string.Equals(
            configuration["Shoprite:InvoiceSubmissionMode"],
            "RealQa",
            StringComparison.OrdinalIgnoreCase))
        {
            return services.AddShopriteClient(configuration);
        }

        services.AddSingleton<IShopriteInvoiceClient, DisabledShopriteInvoiceClient>();
        return services;
    }

    private sealed class DisabledShopriteInvoiceClient : IShopriteInvoiceClient
    {
        public Task<ShopriteInvoiceResponse> SubmitAsync(string xml, CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                "Worker invoice submission is disabled unless Shoprite:InvoiceSubmissionMode is RealQa.");
    }
}
