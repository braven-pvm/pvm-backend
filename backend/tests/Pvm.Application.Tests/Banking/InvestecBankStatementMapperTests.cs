using Pvm.Application.Banking;
using Pvm.Application.Investec;
using Xunit;

namespace Pvm.Application.Tests.Banking;

public class InvestecBankStatementMapperTests
{
    private static readonly DateOnly WindowStart = new(2026, 8, 1);
    private static readonly DateOnly WindowEnd = new(2026, 8, 31);
    private readonly InvestecBankStatementMapper _mapper = new();

    private static InvestecTransaction Txn(
        decimal amount,
        string? direction = null,
        int day = 5,
        string description = "TXN",
        decimal? runningBalance = null,
        string? transactionId = null,
        string accountId = "1300000158",
        string? reference = null,
        string? cardNumber = null) =>
        new(
            AccountId: accountId,
            Description: description,
            Amount: amount,
            TransactionDate: new DateOnly(2026, 8, day),
            Direction: direction,
            RunningBalance: runningBalance,
            TransactionId: transactionId,
            Reference: reference,
            CardNumber: cardNumber);

    [Fact]
    public void Map_splits_credit_and_debit_into_receipt_and_disbursement()
    {
        var result = _mapper.Map("INVESTEC", WindowStart, WindowEnd, new[]
        {
            Txn(100m, "CREDIT", day: 3),
            Txn(50m, "DEBIT", day: 4),
        });

        Assert.Equal(2, result.Lines.Count);
        Assert.Equal(100m, result.Lines[0].Receipt);
        Assert.Equal(0m, result.Lines[0].Disbursement);
        Assert.Equal(0m, result.Lines[1].Receipt);
        Assert.Equal(50m, result.Lines[1].Disbursement);
    }

    [Fact]
    public void Map_treats_negative_amount_without_direction_as_disbursement()
    {
        var result = _mapper.Map("INVESTEC", WindowStart, WindowEnd, new[] { Txn(-25m) });

        Assert.Equal(0m, result.Lines[0].Receipt);
        Assert.Equal(25m, result.Lines[0].Disbursement);
    }

    [Fact]
    public void Map_uses_investec_transaction_id_when_present()
    {
        var result = _mapper.Map("INVESTEC", WindowStart, WindowEnd, new[]
        {
            Txn(100m, "CREDIT", transactionId: "FT26010008488997"),
        });

        Assert.Equal("FT26010008488997", result.Lines[0].ExtTranId);
    }

    [Fact]
    public void Map_generates_stable_deterministic_id_when_no_transaction_id()
    {
        var txn = Txn(100m, "CREDIT", runningBalance: 500m);

        var first = _mapper.Map("INVESTEC", WindowStart, WindowEnd, new[] { txn });
        var second = _mapper.Map("INVESTEC", WindowStart, WindowEnd, new[] { txn });

        Assert.StartsWith("INV-", first.Lines[0].ExtTranId);
        Assert.Equal(first.Lines[0].ExtTranId, second.Lines[0].ExtTranId);
    }

    [Fact]
    public void Map_generates_distinct_ids_for_identical_same_day_amounts_using_running_balance()
    {
        var result = _mapper.Map("INVESTEC", WindowStart, WindowEnd, new[]
        {
            Txn(50m, "DEBIT", day: 6, description: "BANK FEE", runningBalance: 950m),
            Txn(50m, "DEBIT", day: 6, description: "BANK FEE", runningBalance: 900m),
        });

        Assert.NotEqual(result.Lines[0].ExtTranId, result.Lines[1].ExtTranId);
    }

    [Fact]
    public void Map_orders_lines_by_booking_date()
    {
        var result = _mapper.Map("INVESTEC", WindowStart, WindowEnd, new[]
        {
            Txn(10m, "CREDIT", day: 20, description: "LATE"),
            Txn(10m, "CREDIT", day: 2, description: "EARLY"),
        });

        Assert.Equal("EARLY", result.Lines[0].Description);
        Assert.Equal("LATE", result.Lines[1].Description);
    }

    [Fact]
    public void Map_derives_balances_from_last_running_balance_and_net_movement()
    {
        var result = _mapper.Map("INVESTEC", WindowStart, WindowEnd, new[]
        {
            Txn(100m, "CREDIT", day: 3, runningBalance: 1100m),
            Txn(30m, "DEBIT", day: 4, runningBalance: 1070m),
        });

        Assert.Equal(1070m, result.EndingBalance);
        Assert.Equal(1000m, result.BeginningBalance);
    }

    [Fact]
    public void Map_sets_statement_header_fields()
    {
        var result = _mapper.Map("INVESTEC-OPS", WindowStart, WindowEnd, new[]
        {
            Txn(10m, "CREDIT", day: 7),
        });

        Assert.Equal("INVESTEC-OPS", result.CashAccount);
        Assert.Equal(WindowEnd, result.StatementDate);
        Assert.Equal(WindowEnd, result.EndBalanceDate);
        Assert.Equal(new DateOnly(2026, 8, 7), result.StartBalanceDate);
    }
}
