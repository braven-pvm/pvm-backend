using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pvm.Application.Submissions;
using Pvm.Infrastructure.Persistence.Repositories;
using Pvm.Infrastructure.Shoprite;
using Pvm.Application.Messaging;
using Pvm.Infrastructure.Messaging;
using Pvm.Infrastructure.Operations;
using Pvm.Infrastructure.Acumatica;
using Pvm.Infrastructure.Automation;
using Pvm.Application.Automation;

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
        services.AddScoped<ShopriteInventoryMappingBootstrapService>();
        services.AddScoped<ShopriteInventoryMappingService>();
        services.AddScoped<IIntegrationCommandQueue, IntegrationCommandQueue>();
        services.AddScoped<OutboxRepository>();
        services.AddScoped<IntegrationDeliveryRepository>();
        services.AddScoped<IntegrationRunService>();
        services.AddScoped<ShopritePurchaseOrderRefreshRunQueue>();
        services.AddScoped<AcumaticaInvoiceReconciliationRunQueue>();
        services.AddScoped<AcumaticaInvoiceReconciliationFreshnessService>();
        services.AddScoped<AcumaticaPushNotificationInbox>();
        services.AddScoped<AcumaticaPushNotificationHealthService>();
        services.AddScoped<ShopritePurchaseOrderFreshnessService>();
        services.AddScoped<AutomationPolicyService>();
        services.AddScoped<IAutomationSubmissionGate>(provider => provider.GetRequiredService<AutomationPolicyService>());
        return services;
    }
}
