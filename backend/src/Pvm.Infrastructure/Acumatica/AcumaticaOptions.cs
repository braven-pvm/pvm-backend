namespace Pvm.Infrastructure.Acumatica;

public sealed record AcumaticaOptions
{
    public AcumaticaInvoiceSourceMode InvoiceSourceMode { get; init; } = AcumaticaInvoiceSourceMode.Fixture;

    public string? BaseUrl { get; init; }

    public string? Company { get; init; }

    public string? Branch { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    public string EndpointName { get; init; } = "Default";

    public string EndpointVersion { get; init; } = "24.200.001";

    public string CountryCode { get; init; } = "ZA";

    public string[] CustomerAccounts { get; init; } = [];

    public string[] ParentCustomerAccounts { get; init; } = [];

    public DateTimeOffset? InvoiceDateFrom { get; init; }

    public int PageSize { get; init; } = 100;
}

public enum AcumaticaInvoiceSourceMode
{
    Fixture,
    RealQa
}
