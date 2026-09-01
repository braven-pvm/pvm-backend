using Microsoft.Extensions.Options;
using Pvm.Application.Banking;
using Pvm.Application.Investec;
using Pvm.Infrastructure.Investec;
using Xunit;

namespace Pvm.Infrastructure.Tests.Investec;

public sealed class InvestecBankFeedRefreshServiceTests
{
    [Fact]
    public async Task RefreshAsync_pulls_maps_and_imports()
    {
        var investec = new StubInvestecClient(new[]
        {
            new InvestecTransaction("1300", "SALARY", 1000m, new DateOnly(2026, 8, 3), Direction: "CREDIT", RunningBalance: 5000m),
            new InvestecTransaction("1300", "FEE", 55m, new DateOnly(2026, 8, 4), Direction: "DEBIT", RunningBalance: 4945m),
        });
        var acumatica = new StubAcumaticaClient(new BankStatementImportResult("STMT-1", 2));
        var service = new InvestecBankFeedRefreshService(
            investec,
            new InvestecBankStatementMapper(),
            acumatica,
            Options.Create(DefaultOptions()));

        var result = await service.RefreshAsync(
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31),
            CancellationToken.None);

        Assert.Equal(2, result.TransactionsRetrieved);
        Assert.Equal(2, result.LinesImported);
        Assert.Equal("STMT-1", result.StatementReference);

        Assert.NotNull(acumatica.LastStatement);
        Assert.Equal("INVESTEC-OPS", acumatica.LastStatement!.CashAccount);
        Assert.Equal(2, acumatica.LastStatement.Lines.Count);
        Assert.Equal(1000m, acumatica.LastStatement.Lines[0].Receipt);
        Assert.Equal(55m, acumatica.LastStatement.Lines[1].Disbursement);
    }

    [Fact]
    public async Task RefreshAsync_skips_import_when_no_transactions()
    {
        var investec = new StubInvestecClient(Array.Empty<InvestecTransaction>());
        var acumatica = new StubAcumaticaClient(new BankStatementImportResult("UNUSED", 0));
        var service = new InvestecBankFeedRefreshService(
            investec,
            new InvestecBankStatementMapper(),
            acumatica,
            Options.Create(DefaultOptions()));

        var result = await service.RefreshAsync(
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31),
            CancellationToken.None);

        Assert.Equal(0, result.TransactionsRetrieved);
        Assert.Equal(0, result.LinesImported);
        Assert.Null(result.StatementReference);
        Assert.Null(acumatica.LastStatement);
    }

    private static InvestecOptions DefaultOptions()
        => new()
        {
            BaseUrl = "https://openapi.investec.example",
            ClientId = "client",
            ClientSecret = "secret",
            AccountId = "1300",
            CashAccount = "INVESTEC-OPS",
        };

    private sealed class StubInvestecClient(IReadOnlyList<InvestecTransaction> transactions)
        : IInvestecTransactionClient
    {
        public Task<IReadOnlyList<InvestecTransaction>> GetTransactionsAsync(
            string accountId,
            DateOnly fromDate,
            DateOnly toDate,
            CancellationToken cancellationToken = default)
            => Task.FromResult(transactions);
    }

    private sealed class StubAcumaticaClient(BankStatementImportResult result)
        : IAcumaticaBankStatementClient
    {
        public BankStatementImport? LastStatement { get; private set; }

        public Task<BankStatementImportResult> ImportAsync(
            BankStatementImport statement,
            CancellationToken cancellationToken = default)
        {
            LastStatement = statement;
            return Task.FromResult(result);
        }
    }
}
