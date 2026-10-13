namespace Pvm.Infrastructure.Nedbank;

/// <summary>
/// Configuration for the Nedbank manual statement import: which Acumatica cash account each
/// bank account's statements write into.
/// </summary>
public sealed record NedbankOptions
{
    public const string SectionName = "Nedbank";

    /// <summary>
    /// Bank account number as it appears in the statement file, mapped to the Acumatica Cash
    /// Account CD it imports into.
    ///
    /// There is deliberately no default. A statement whose account is not listed here is
    /// refused, because a fallback would post one account's statement into another silently.
    /// That happened once already with a Western Cape statement imported by hand.
    /// </summary>
    public Dictionary<string, string> Accounts { get; init; } = [];
}
