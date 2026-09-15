namespace Pvm.Application.Banking;

/// <summary>
/// What a downloaded statement file would do to Acumatica, worked out without writing anything.
/// The person importing reads this and decides. It answers the questions an accountant asks:
/// is this the right account, does the opening balance continue from the last import, what is
/// new, and does anything overlap what Acumatica already holds.
/// </summary>
public sealed record BankStatementPreview(
    string CashAccount,
    string? SourceAccountNumber,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal MoneyIn,
    decimal MoneyOut,
    decimal OpeningBalance,
    decimal ClosingBalance,
    DateOnly? ImportedThrough,
    decimal? ImportedThroughBalance,
    bool BalancesCarryForward,
    bool OverlapsImportedPeriod,
    int AlreadyImportedCount,
    int NewCount,
    IReadOnlyList<DateOnly> UncoveredDates,
    IReadOnlyList<BankStatementPreviewLine> Lines)
{
    /// <summary>Total transactions in the file, after the parser drops zero-amount noise.</summary>
    public int TransactionCount => Lines.Count;

    /// <summary>True when every line is already in Acumatica, so an import would change nothing.</summary>
    public bool NothingToImport => Lines.Count > 0 && NewCount == 0;
}

/// <summary>
/// One transaction as the preview shows it. <see cref="AlreadyImported"/> marks a line whose
/// id Acumatica already holds, so the import would skip it.
/// </summary>
public sealed record BankStatementPreviewLine(
    DateOnly Date,
    string Description,
    decimal Receipt,
    decimal Disbursement,
    bool AlreadyImported);

/// <summary>A statement that Acumatica already holds, as the header row alone.</summary>
public sealed record BankStatementSummary(
    string ReferenceNbr,
    DateOnly StartBalanceDate,
    DateOnly EndBalanceDate,
    decimal BeginningBalance,
    decimal EndingBalance,
    int LineCount);
