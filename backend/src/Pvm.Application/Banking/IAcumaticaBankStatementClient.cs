namespace Pvm.Application.Banking;

/// <summary>
/// Imports a bank statement into Acumatica Cash Management via the custom
/// <c>PVMBankFeed</c> contract endpoint over Import Bank Transactions (CA306500).
/// Acumatica de-duplicates the lines on <c>Ext. Tran. ID</c>.
/// </summary>
public interface IAcumaticaBankStatementClient
{
    Task<BankStatementImportResult> ImportAsync(
        BankStatementImport statement,
        CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a bank statement import: the Acumatica reference number and line count sent.</summary>
public sealed record BankStatementImportResult(string ReferenceNbr, int LineCount);
