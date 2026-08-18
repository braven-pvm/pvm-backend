namespace Pvm.Infrastructure.Nedbank;

/// <summary>
/// Configuration for the Nedbank manual statement import: the Acumatica Cash Account CD that a
/// parsed Nedbank statement writes into.
/// </summary>
public sealed record NedbankOptions
{
    public const string SectionName = "Nedbank";

    /// <summary>Acumatica Cash Account CD that Nedbank statements import into.</summary>
    public string? CashAccount { get; init; }
}
