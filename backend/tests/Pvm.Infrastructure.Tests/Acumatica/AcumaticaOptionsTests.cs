using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pvm.Infrastructure.Acumatica;

namespace Pvm.Infrastructure.Tests.Acumatica;

public sealed class AcumaticaOptionsTests
{
    [Fact]
    public void Live_reading_without_a_cutover_date_is_refused()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Acumatica:InvoiceSourceMode"] = "Real",
            ["Acumatica:BaseUrl"] = "https://acumatica.example",
        });

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<AcumaticaOptions>>().Value);

        Assert.Contains("InvoiceDateFrom", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_reading_with_a_cutover_date_is_allowed()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Acumatica:InvoiceSourceMode"] = "Real",
            ["Acumatica:BaseUrl"] = "https://acumatica.example",
            ["Acumatica:InvoiceDateFrom"] = "2026-09-01T00:00:00+02:00",
        });

        var options = provider.GetRequiredService<IOptions<AcumaticaOptions>>().Value;

        Assert.Equal(AcumaticaInvoiceSourceMode.Real, options.InvoiceSourceMode);
        Assert.NotNull(options.InvoiceDateFrom);
    }

    [Fact]
    public void The_fixture_source_needs_no_cutover_date()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Acumatica:InvoiceSourceMode"] = "Fixture",
        });

        var options = provider.GetRequiredService<IOptions<AcumaticaOptions>>().Value;

        Assert.Equal(AcumaticaInvoiceSourceMode.Fixture, options.InvoiceSourceMode);
        Assert.Null(options.InvoiceDateFrom);
    }

    private static ServiceProvider Build(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddOptions<AcumaticaOptions>()
            .Bind(configuration.GetSection("Acumatica"))
            .Validate(
                options => options.InvoiceSourceMode != AcumaticaInvoiceSourceMode.Real
                    || options.InvoiceDateFrom is not null,
                "Acumatica:InvoiceDateFrom is required when reading live invoices.");
        return services.BuildServiceProvider();
    }
}
