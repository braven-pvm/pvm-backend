namespace Pvm.Infrastructure.Investec;

/// <summary>
/// Configuration for the Investec BCB Transaction History API client. Credentials come from
/// the Investec Integration Manager; secrets belong in Key Vault, never in the repo.
/// </summary>
public sealed record InvestecOptions
{
    public const string SectionName = "Investec";

    /// <summary>Gateway base URL, e.g. https://openapisandbox.investec.com or https://openapi.investec.com.</summary>
    public string? BaseUrl { get; init; }

    public string? ClientId { get; init; }

    public string? ClientSecret { get; init; }

    /// <summary>The <c>x-api-key</c> issued alongside the client credentials.</summary>
    public string? ApiKey { get; init; }

    /// <summary>System-assigned account id for the transactions endpoint (from the Integration Manager).</summary>
    public string? AccountId { get; init; }

    /// <summary>The Acumatica Cash Account CD this Investec account's transactions import into.</summary>
    public string? CashAccount { get; init; }

    /// <summary>Safety cap on pages walked per pull to avoid an unbounded loop.</summary>
    public int MaxPages { get; init; } = 200;

    /// <summary>
    /// How many days back each scheduled refresh reaches. The window overlaps previous runs on
    /// purpose, so a missed or failed run is collected by the next one. Re-importing a day costs
    /// nothing, because the write client drops transactions Acumatica already holds.
    /// </summary>
    public int RefreshLookbackDays { get; init; } = 7;

    /// <summary>
    /// The first date this feed owns. A scheduled refresh never reaches earlier, however far the
    /// lookback would otherwise go. Transactions before this date belong to the manual import,
    /// which numbers them differently, so pulling them would create duplicates in Acumatica.
    /// </summary>
    public DateOnly? FeedStartDate { get; init; }
}
