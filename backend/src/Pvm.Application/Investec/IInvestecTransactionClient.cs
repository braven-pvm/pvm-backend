namespace Pvm.Application.Investec;

/// <summary>
/// Retrieves transaction history for a single Investec account over a date range.
/// Implementations own OAuth token acquisition and page-through of the Investec API.
/// </summary>
public interface IInvestecTransactionClient
{
    /// <summary>
    /// Returns every transaction for <paramref name="accountId"/> booked between
    /// <paramref name="fromDate"/> and <paramref name="toDate"/> (inclusive), following
    /// all pages. <paramref name="accountId"/> is the Investec system-assigned account id
    /// (from the Integration Manager), not the human account number.
    /// </summary>
    Task<IReadOnlyList<InvestecTransaction>> GetTransactionsAsync(
        string accountId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);
}
