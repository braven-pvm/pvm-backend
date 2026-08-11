using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Pvm.Application.Investec;

namespace Pvm.Application.Banking;

/// <summary>
/// Transforms a batch of Investec transactions for one account/date-window into a single
/// <see cref="BankStatementImport"/> ready to POST to the Acumatica <c>PVMBankFeed</c> endpoint.
/// </summary>
public sealed class InvestecBankStatementMapper
{
    public BankStatementImport Map(
        string cashAccount,
        DateOnly windowStart,
        DateOnly windowEnd,
        IReadOnlyList<InvestecTransaction> transactions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cashAccount);
        ArgumentNullException.ThrowIfNull(transactions);

        // Deterministic, stable ordering: booking date, then original arrival order.
        var ordered = transactions
            .Select((transaction, index) => (transaction, index))
            .OrderBy(item => item.transaction.BookingDate)
            .ThenBy(item => item.index)
            .Select(item => item.transaction)
            .ToList();

        var lines = new List<BankStatementLine>(ordered.Count);
        foreach (var transaction in ordered)
        {
            var signed = SignedAmount(transaction);
            lines.Add(new BankStatementLine(
                ExtTranId: ResolveExtTranId(transaction),
                TranDate: transaction.BookingDate,
                Description: transaction.Description,
                Receipt: signed > 0m ? signed : 0m,
                Disbursement: signed < 0m ? -signed : 0m,
                ExtRefNbr: NullIfBlank(transaction.Reference),
                CardNumber: NullIfBlank(transaction.CardNumber)));
        }

        var (beginning, ending) = ResolveBalances(ordered);
        var startDate = ordered.Count > 0 ? ordered[0].BookingDate : windowStart;

        return new BankStatementImport(
            CashAccount: cashAccount,
            StatementDate: windowEnd,
            StartBalanceDate: startDate,
            EndBalanceDate: windowEnd,
            BeginningBalance: beginning,
            EndingBalance: ending,
            Lines: lines);
    }

    // Money in is positive, money out is negative. Prefer an explicit CREDIT/DEBIT
    // direction; otherwise trust the sign carried on Amount.
    private static decimal SignedAmount(InvestecTransaction transaction)
    {
        var magnitude = Math.Abs(transaction.Amount);
        if (!string.IsNullOrWhiteSpace(transaction.Direction))
        {
            if (transaction.Direction.Equals("CREDIT", StringComparison.OrdinalIgnoreCase))
            {
                return magnitude;
            }

            if (transaction.Direction.Equals("DEBIT", StringComparison.OrdinalIgnoreCase))
            {
                return -magnitude;
            }
        }

        return transaction.Amount;
    }

    private static string ResolveExtTranId(InvestecTransaction transaction) =>
        string.IsNullOrWhiteSpace(transaction.TransactionId)
            ? DeterministicId(transaction)
            : transaction.TransactionId.Trim();

    // Stable synthetic id for banks that do not return a transaction id. The running
    // balance is folded in so two identical same-day amounts get distinct ids, and because
    // it is deterministic per transaction the id stays stable across overlapping re-pulls.
    private static string DeterministicId(InvestecTransaction transaction)
    {
        var key = string.Join(
            '|',
            transaction.AccountId,
            transaction.BookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            SignedAmount(transaction).ToString(CultureInfo.InvariantCulture),
            transaction.RunningBalance?.ToString(CultureInfo.InvariantCulture) ?? "-",
            transaction.Description);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return "INV-" + Convert.ToHexString(hash)[..28];
    }

    // Prefer bank-provided running balances; otherwise roll the net movement up from zero.
    private static (decimal Beginning, decimal Ending) ResolveBalances(IReadOnlyList<InvestecTransaction> ordered)
    {
        var net = ordered.Sum(SignedAmount);
        var lastWithBalance = ordered.LastOrDefault(transaction => transaction.RunningBalance.HasValue);
        if (lastWithBalance is not null)
        {
            var ending = lastWithBalance.RunningBalance!.Value;
            return (ending - net, ending);
        }

        return (0m, net);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
