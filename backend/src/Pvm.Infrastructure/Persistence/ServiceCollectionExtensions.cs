using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pvm.Application.Submissions;
using Pvm.Infrastructure.Persistence.Repositories;
using Pvm.Infrastructure.Shoprite;
using Pvm.Application.Messaging;
using Pvm.Infrastructure.Messaging;

namespace Pvm.Infrastructure.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPvmPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Pvm");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'Pvm' is required.");
        }

        services.AddDbContext<PvmDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IInvoiceCandidateRepository, EfInvoiceCandidateRepository>();
        services.AddScoped<ShopriteSeedInvoiceCandidateService>();
        services.AddScoped<ShopriteInvoiceCandidateMatcher>();
        services.AddScoped<ShopriteInvoiceLineMappingService>();
        services.AddScoped<IIntegrationCommandQueue, IntegrationCommandQueue>();
        services.AddScoped<OutboxRepository>();
        services.AddScoped<IntegrationDeliveryRepository>();
        return services;
    }
}
