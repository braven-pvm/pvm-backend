namespace Pvm.Application.Banking;

/// <summary>
/// A bank statement to import into Acumatica Cash Management for reconciliation.
/// Maps 1:1 to the <c>PVMBankFeed</c> custom endpoint over the Import Bank Transactions
/// screen (CA306500): the header is the statement for a single cash account and the
/// <see cref="Lines"/> are its bank transactions.
/// </summary>
public sealed record BankStatementImport(
    string CashAccount,
    DateOnly StatementDate,
    DateOnly StartBalanceDate,
    DateOnly EndBalanceDate,
    decimal BeginningBalance,
    decimal EndingBalance,
    IReadOnlyList<BankStatementLine> Lines);

/// <summary>
/// A single bank transaction line on an imported statement. <see cref="ExtTranId"/> is the
/// key Acumatica de-duplicates on: re-importing a line with a seen id is skipped. Amounts
/// are split into non-negative <see cref="Receipt"/> (money in) and
/// <see cref="Disbursement"/> (money out); exactly one is non-zero.
/// </summary>
public sealed record BankStatementLine(
    string ExtTranId,
    DateOnly TranDate,
    string Description,
    decimal Receipt,
    decimal Disbursement,
    string? ExtRefNbr = null,
    string? CardNumber = null);
