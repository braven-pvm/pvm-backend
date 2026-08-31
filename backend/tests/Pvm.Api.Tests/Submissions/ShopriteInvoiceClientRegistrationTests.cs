using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pvm.Api.Features.Submissions;
using Pvm.Application.Submissions;
using Pvm.Infrastructure.Shoprite;

namespace Pvm.Api.Tests.Submissions;

public sealed class ShopriteInvoiceClientRegistrationTests
{
    [Fact]
    public void AddConfiguredShopriteInvoiceClient_DefaultsToLocalStub()
    {
        using var provider = BuildProvider([]);

        var client = provider.GetRequiredService<IShopriteInvoiceClient>();

        Assert.IsType<LocalShopriteInvoiceClient>(client);
    }

    [Fact]
    public void AddConfiguredShopriteInvoiceClient_UsesRealClientWhenExplicitlyEnabled()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Shoprite:InvoiceSubmissionMode"] = "RealQa",
            ["Shoprite:BaseUrl"] = "https://shoprite.example/",
            ["Shoprite:Username"] = "api-user",
            ["Shoprite:Password"] = "secret",
            ["Shoprite:ContractId"] = "contract-123"
        });

        var client = provider.GetRequiredService<IShopriteInvoiceClient>();

        Assert.IsType<ShopriteInvoiceClient>(client);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> configurationValues)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();
        var services = new ServiceCollection();
        services.AddConfiguredShopriteInvoiceClient(configuration);

        return services.BuildServiceProvider();
    }
}
