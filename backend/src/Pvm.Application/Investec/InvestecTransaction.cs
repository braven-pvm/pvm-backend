namespace Pvm.Application.Investec;

/// <summary>
/// A single transaction as returned by the Investec BCB Transaction History API
/// (<c>GET /za/bb/v2/accounts/{accountId}/transactions</c>).
/// </summary>
/// <remarks>
/// Field shape is modelled from the Bank Integrations Technical Guide v1.0 and the
/// public Investec transaction structure. It MUST be verified against the sandbox
/// response before go-live; in particular whether <see cref="Amount"/> is a signed
/// value or a positive magnitude paired with <see cref="Direction"/>, and whether a
/// stable <see cref="TransactionId"/> is present (it drives Acumatica de-duplication).
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
