namespace Pvm.Application.Banking;

/// <summary>
/// Reads and writes bank statements in Acumatica Cash Management via the custom
/// <c>PVMBankFeed</c> contract endpoint over Import Bank Transactions (CA306500).
/// Acumatica does NOT de-duplicate the lines itself at import, so the write path filters
/// already-imported lines on <c>Ext. Tran. ID</c> before it sends them.
/// </summary>
public interface IAcumaticaBankStatementClient
{
    Task<BankStatementImportResult> ImportAsync(
        BankStatementImport statement,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The most recent statement on a cash account, or <c>null</c> when it holds none. Its
    /// <see cref="BankStatementSummary.EndBalanceDate"/> is how far the account is imported.
    /// </summary>
    Task<BankStatementSummary?> GetLatestStatementAsync(
        string cashAccount,
        CancellationToken cancellationToken = default);

    /// <summary>Recent statements on a cash account, newest first.</summary>
    Task<IReadOnlyList<BankStatementSummary>> GetRecentStatementsAsync(
        string cashAccount,
        int count,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The <c>Ext. Tran. ID</c>s already on a cash account for transactions dated within
    /// the window, used to tell which lines of a file are new.
    /// </summary>
    Task<IReadOnlySet<string>> GetImportedTransactionIdsAsync(
        string cashAccount,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a bank statement import: the Acumatica reference number and line count sent.</summary>
public sealed record BankStatementImportResult(string ReferenceNbr, int LineCount);
