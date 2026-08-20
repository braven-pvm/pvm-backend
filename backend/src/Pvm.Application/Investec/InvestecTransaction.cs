namespace Pvm.Application.Investec;

/// <summary>
/// A single transaction as returned by the Investec BCB Transaction History API
/// (<c>GET /za/bb/v2/accounts/{accountId}/transactions</c>).
/// </summary>
/// <remarks>
/// This is a normalised shape; <c>InvestecTransactionClient</c> maps the live BCB response
/// onto it. Verified against the live API 2026-08-20: money in/out arrives as
/// <c>deposit</c>/<c>withdrawal</c> (mapped to a positive <see cref="Amount"/> +
/// <see cref="Direction"/>), and a stable <c>transactionId</c> is present, so
/// <see cref="TransactionId"/> drives Acumatica de-duplication (no synthetic hash needed).
/// </remarks>
public sealed record InvestecTransaction(
    string AccountId,
    string Description,
    decimal Amount,
    DateOnly TransactionDate,
    string? Direction = null,
    string? TransactionType = null,
    string? Status = null,
    string? CardNumber = null,
    DateOnly? PostingDate = null,
    DateOnly? ValueDate = null,
    decimal? RunningBalance = null,
    string? Reference = null,
    string? TransactionId = null)
{
    /// <summary>Date the transaction is booked against the bank statement.</summary>
    public DateOnly BookingDate => PostingDate ?? TransactionDate;
}
