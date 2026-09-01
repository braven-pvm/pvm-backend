using Microsoft.Extensions.Options;
using Pvm.Application.Banking;
using Pvm.Application.Investec;

namespace Pvm.Infrastructure.Investec;

/// <summary>
/// Orchestrates one bank-feed refresh: pull Investec transactions for the configured account
/// and window, map them to an Acumatica bank statement, and import it (Acumatica de-duplicates
/// on Ext. Tran. ID, so overlapping windows are safe). Skips the import when there are no lines.
/// </summary>
public sealed class InvestecBankFeedRefreshService(
    IInvestecTransactionClient investecClient,
    InvestecBankStatementMapper mapper,
    IAcumaticaBankStatementClient acumaticaClient,
    IOptions<InvestecOptions> options)
{
    private readonly InvestecOptions _options = options.Value;

    public async Task<BankFeedRefreshResult> RefreshAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.AccountId))
        {
            throw new InvalidOperationException("Investec AccountId is required to refresh the bank feed.");
        }

        if (string.IsNullOrWhiteSpace(_options.CashAccount))
        {
            throw new InvalidOperationException("Investec CashAccount (Acumatica target) is required to refresh the bank feed.");
        }

        var transactions = await investecClient.GetTransactionsAsync(
            _options.AccountId,
            fromDate,
            toDate,
            cancellationToken);

        var statement = mapper.Map(_options.CashAccount, fromDate, toDate, transactions);
        if (statement.Lines.Count == 0)
        {
            return new BankFeedRefreshResult(transactions.Count, 0, null);
        }

        var import = await acumaticaClient.ImportAsync(statement, cancellationToken);
        return new BankFeedRefreshResult(transactions.Count, import.LineCount, import.ReferenceNbr);
    }
}

/// <summary>Outcome of a bank-feed refresh.</summary>
public sealed record BankFeedRefreshResult(
    int TransactionsRetrieved,
    int LinesImported,
    string? StatementReference);
