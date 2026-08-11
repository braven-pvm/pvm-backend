namespace Pvm.Infrastructure.Acumatica;

/// <summary>
/// Locates the custom Acumatica contract endpoint that exposes Import Bank Transactions
/// (CA306500) for writing bank statements. Uses the same instance/credentials as
/// <see cref="AcumaticaOptions"/>; only the endpoint coordinates differ.
/// </summary>
public sealed record AcumaticaBankFeedOptions
{
    public const string SectionName = "AcumaticaBankFeed";

    public string EndpointName { get; init; } = "PVMBankFeed";

    public string EndpointVersion { get; init; } = "01.000.001";

    public string TopLevelEntity { get; init; } = "BankStatement";
}
