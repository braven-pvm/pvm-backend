namespace Pvm.Infrastructure.Reporting;

/// <summary>Settings of the Acumatica sell-in load (configuration section "SellIn").</summary>
public sealed record SellInSyncOptions
{
    public const string SectionName = "SellIn";

    /// <summary>Off by default, so that no environment loads sales until its job is set up.</summary>
    public bool Enabled { get; init; }

    /// <summary>The Generic Inquiry, as its OData entity set is named.</summary>
    public string InquiryName { get; init; } = "PVM-Sales-Lines";

    /// <summary>
    /// Posting periods before the current one that each run reads again. Credit memos and late
    /// postings can land in an earlier period, and a missed run is collected by the next one.
    /// </summary>
    public int PeriodsBack { get; init; } = 2;

    /// <summary>
    /// Posting periods after the current one that each run reads. PVM closes a period near the
    /// 25th, so at the end of a month the documents already post to the next period.
    /// </summary>
    public int PeriodsAhead { get; init; } = 1;

    /// <summary>
    /// The first posting period the load may read, such as "012019". A run never reads before it.
    /// Empty means no floor.
    /// </summary>
    public string? FirstPeriod { get; init; }

    /// <summary>Seconds one period read may take.</summary>
    public int ReadTimeoutSeconds { get; init; } = 300;

    /// <summary>Keep each raw response in the payload archive.</summary>
    public bool ArchiveRawResponses { get; init; } = true;
}
