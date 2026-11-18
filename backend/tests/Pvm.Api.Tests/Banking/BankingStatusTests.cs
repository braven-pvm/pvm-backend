using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pvm.Api.Features.Banking;
using Pvm.Application.Banking;
using Pvm.Infrastructure.Investec;
using Pvm.Infrastructure.Nedbank;

namespace Pvm.Api.Tests.Banking;

public sealed class BankingStatusTests
{
    private static readonly NedbankOptions Nedbank = new()
    {
        Accounts =
        {
            ["1"] = "WCNEDBANK",
            ["2"] = "ECNEDBANK",
            ["3"] = "PVMNEDBANK"
        }
    };

    private static readonly InvestecOptions Investec = new() { CashAccount = "INVESTEC" };

    private static readonly DateTimeOffset FirstRead = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task All_accounts_are_read_in_one_call_and_keep_their_order()
    {
        var client = new StubClient();

        var read = await BankImportEndpoints.ReadStatusAsync(client, Nedbank, Investec, CancellationToken.None);

        Assert.Empty(read.Failures);
        Assert.Equal(
            ["ECNEDBANK", "PVMNEDBANK", "WCNEDBANK", "INVESTEC"],
            read.Response.Accounts.Select(account => account.CashAccount));
        Assert.All(read.Response.Accounts, account => Assert.True(account.Available));
        Assert.Equal(1, client.BatchCalls);
        Assert.Equal(["ECNEDBANK", "PVMNEDBANK", "WCNEDBANK", "INVESTEC"], client.LastBatch);
    }

    [Fact]
    public async Task A_failed_account_read_reports_that_account_and_its_reason()
    {
        var client = new StubClient { Failing = { "PVMNEDBANK" } };

        var read = await BankImportEndpoints.ReadStatusAsync(client, Nedbank, Investec, CancellationToken.None);

        Assert.Equal("Acumatica answered 500 (InternalServerError)", Assert.Single(read.Failures).Value);
        var byAccount = read.Response.Accounts.ToDictionary(account => account.CashAccount);
        Assert.False(byAccount["PVMNEDBANK"].Available);
        Assert.True(byAccount["ECNEDBANK"].Available);
        Assert.Equal(new DateOnly(2026, 11, 10), byAccount["ECNEDBANK"].ImportedThrough);
    }

    [Fact]
    public async Task A_visit_gets_the_stored_status_and_does_not_read_Acumatica()
    {
        var client = new StubClient();
        var refresher = NewRefresher(client);
        await refresher.RefreshAsync(CancellationToken.None);

        var first = await refresher.GetAsync(CancellationToken.None);
        var second = await refresher.GetAsync(CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(1, client.BatchCalls);
    }

    [Fact]
    public async Task A_visit_before_the_first_refresh_reads_once()
    {
        var client = new StubClient();
        var refresher = NewRefresher(client);

        await refresher.GetAsync(CancellationToken.None);
        await refresher.GetAsync(CancellationToken.None);

        Assert.Equal(1, client.BatchCalls);
    }

    [Fact]
    public async Task A_failed_account_keeps_its_last_figures_and_the_failure_is_logged_without_secrets()
    {
        var client = new StubClient();
        var logger = new ListLogger();
        var refresher = NewRefresher(client, logger);
        await refresher.RefreshAsync(CancellationToken.None);

        client.Failing.Add("PVMNEDBANK");
        await refresher.RefreshAsync(CancellationToken.None);
        var status = await refresher.GetAsync(CancellationToken.None);

        var pvm = Assert.Single(status.Accounts, account => account.CashAccount == "PVMNEDBANK");
        Assert.True(pvm.Available);
        Assert.True(pvm.Stale);
        Assert.Equal(FirstRead, pvm.ReadAt);
        Assert.Equal(new DateOnly(2026, 11, 10), pvm.ImportedThrough);
        Assert.False(Assert.Single(status.Accounts, account => account.CashAccount == "INVESTEC").Stale);
        var warning = Assert.Single(logger.Messages, message => message.Contains("banking.status.account.failed", StringComparison.Ordinal));
        Assert.Contains("CashAccount=PVMNEDBANK", warning, StringComparison.Ordinal);
        Assert.Contains("Reason=Acumatica answered 500", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("password", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_fresh_read_is_not_stale_and_carries_its_time()
    {
        var refresher = NewRefresher(new StubClient());

        var status = await refresher.GetAsync(CancellationToken.None);

        Assert.All(status.Accounts, account =>
        {
            Assert.False(account.Stale);
            Assert.Equal(FirstRead, account.ReadAt);
        });
    }

    [Fact]
    public async Task An_account_named_twice_is_read_once_and_the_read_does_not_fail()
    {
        var client = new StubClient();

        var read = await BankImportEndpoints.ReadStatusAsync(
            client, Nedbank, new InvestecOptions { CashAccount = "pvmnedbank" }, CancellationToken.None);

        Assert.Equal(["ECNEDBANK", "PVMNEDBANK", "WCNEDBANK"], client.LastBatch);
        Assert.Equal(4, read.Response.Accounts.Count);
        Assert.Empty(read.Failures);
    }

    [Fact]
    public async Task An_import_still_succeeds_when_the_status_refresh_after_it_fails()
    {
        var client = new StubClient();
        var logger = new ListLogger();
        var refresher = NewRefresher(client, logger);
        await refresher.RefreshAsync(CancellationToken.None);
        var importer = new NedbankStatementImportService(
            new NedbankOfxParser(),
            client,
            Options.Create(new NedbankOptions { Accounts = { ["1644294346"] = "PVMNEDBANK" } }));

        client.ThrowOnBatch = true;
        var result = await BankImportEndpoints.ImportNedbankAsync(
            OfxFile(), importer, refresher, CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, (result as IStatusCodeHttpResult)?.StatusCode);
        Assert.Equal(1, client.Imports);
        Assert.Contains(logger.Messages, message => message.Contains("banking.status.refresh.failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_account_that_never_read_shows_as_unavailable()
    {
        var client = new StubClient { Failing = { "INVESTEC" } };
        var refresher = NewRefresher(client);

        var status = await refresher.GetAsync(CancellationToken.None);

        Assert.False(Assert.Single(status.Accounts, account => account.CashAccount == "INVESTEC").Available);
    }

    [Fact]
    public async Task The_status_is_refreshed_in_the_background_on_its_interval()
    {
        var client = new StubClient();
        var refresher = NewRefresher(client, interval: TimeSpan.FromMilliseconds(50));

        await refresher.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => client.BatchCalls >= 3);
        await refresher.StopAsync(CancellationToken.None);

        Assert.True(client.BatchCalls >= 3);
    }

    [Fact]
    public void The_default_refresh_interval_is_between_two_and_three_minutes()
    {
        var interval = new BankingStatusOptions().RefreshInterval;

        Assert.InRange(interval, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3));
    }

    [Fact]
    public async Task A_successful_nedbank_import_refreshes_the_status_before_it_answers()
    {
        var client = new StubClient();
        var refresher = NewRefresher(client);
        await refresher.RefreshAsync(CancellationToken.None);
        var importer = new NedbankStatementImportService(
            new NedbankOfxParser(),
            client,
            Options.Create(new NedbankOptions { Accounts = { ["1644294346"] = "PVMNEDBANK" } }));

        client.EndDate = new DateOnly(2026, 11, 11);
        var result = await BankImportEndpoints.ImportNedbankAsync(
            OfxFile(), importer, refresher, CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, (result as IStatusCodeHttpResult)?.StatusCode);
        Assert.Equal(1, client.Imports);
        Assert.Equal(2, client.BatchCalls);
        var status = await refresher.GetAsync(CancellationToken.None);
        Assert.All(status.Accounts, account => Assert.Equal(new DateOnly(2026, 11, 11), account.ImportedThrough));
    }

    [Fact]
    public async Task A_refused_nedbank_import_does_not_refresh_the_status()
    {
        var client = new StubClient();
        var refresher = NewRefresher(client);
        await refresher.RefreshAsync(CancellationToken.None);
        var importer = new NedbankStatementImportService(
            new NedbankOfxParser(),
            client,
            Options.Create(new NedbankOptions { Accounts = { ["9999999999"] = "OTHER" } }));

        var result = await BankImportEndpoints.ImportNedbankAsync(
            OfxFile(), importer, refresher, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, (result as IStatusCodeHttpResult)?.StatusCode);
        Assert.Equal(1, client.BatchCalls);
    }

    private static BankingStatusRefresher NewRefresher(
        StubClient client,
        ListLogger? logger = null,
        TimeSpan? interval = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAcumaticaBankStatementClient>(client);
        var provider = services.BuildServiceProvider();
        return new BankingStatusRefresher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(Nedbank),
            Options.Create(Investec),
            Options.Create(new BankingStatusOptions { RefreshInterval = interval ?? TimeSpan.FromMinutes(2.5) }),
            new SteppingTime(FirstRead),
            logger ?? new ListLogger());
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < until)
        {
            await Task.Delay(20);
        }
    }

    private static IFormFile OfxFile()
    {
        const string ofx =
"""
<?xml version="1.0" encoding="US-ASCII"?>
<OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS>
<BANKACCTFROM><ACCTID>1644294346</ACCTID></BANKACCTFROM>
<BANKTRANLIST><DTSTART>20261111</DTSTART><DTEND>20261111</DTEND>
<STMTTRN><DTPOSTED>20261111</DTPOSTED><TRNAMT>-100.00</TRNAMT><FITID>00000675900</FITID><NAME>DEBIT ONE</NAME></STMTTRN>
</BANKTRANLIST><LEDGERBAL><BALAMT>150.00</BALAMT><DTASOF>20261111</DTASOF></LEDGERBAL>
</STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>
""";
        var bytes = Encoding.ASCII.GetBytes(ofx);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "statement.ofx");
    }

    /// <summary>The first read happens at the start time; each later read is a minute later.</summary>
    private sealed class SteppingTime(DateTimeOffset start) : TimeProvider
    {
        private int _reads;

        public override DateTimeOffset GetUtcNow() => start.AddMinutes(Interlocked.Increment(ref _reads) - 1);
    }

    private sealed class ListLogger : ILogger<BankingStatusRefresher>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (Messages)
            {
                Messages.Add(formatter(state, exception));
            }
        }
    }

    private sealed class StubClient : IAcumaticaBankStatementClient
    {
        private int _batchCalls;

        public HashSet<string> Failing { get; } = new(StringComparer.OrdinalIgnoreCase);

        public DateOnly EndDate { get; set; } = new(2026, 11, 10);

        public bool ThrowOnBatch { get; set; }

        public int BatchCalls => Volatile.Read(ref _batchCalls);

        public IReadOnlyList<string> LastBatch { get; private set; } = [];

        public int Imports { get; private set; }

        public Task<BankStatementImportResult> ImportAsync(
            BankStatementImport statement,
            CancellationToken cancellationToken = default)
        {
            Imports++;
            return Task.FromResult(new BankStatementImportResult("STMT-1", statement.Lines.Count));
        }

        public Task<BankStatementSummary?> GetLatestStatementAsync(
            string cashAccount,
            CancellationToken cancellationToken = default)
            => Task.FromResult<BankStatementSummary?>(null);

        public Task<IReadOnlyList<BankStatementSummary>> GetRecentStatementsAsync(
            string cashAccount,
            int count,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The status must read all accounts in one call.");

        public Task<IReadOnlyList<CashAccountStatements>> ReadRecentStatementsAsync(
            IReadOnlyList<string> cashAccounts,
            int count,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _batchCalls);
            if (ThrowOnBatch)
            {
                throw new InvalidOperationException("Acumatica sign-in failed with status 500 (InternalServerError).");
            }

            LastBatch = cashAccounts.ToList();
            IReadOnlyList<CashAccountStatements> results = cashAccounts
                .Select(cashAccount => Failing.Contains(cashAccount)
                    ? new CashAccountStatements(cashAccount, [], "Acumatica answered 500 (InternalServerError)")
                    : new CashAccountStatements(
                        cashAccount,
                        [new BankStatementSummary("001", new DateOnly(2026, 11, 1), EndDate, 0m, 10m, 2)],
                        null))
                .ToList();
            return Task.FromResult(results);
        }

        public Task<IReadOnlySet<string>> GetImportedTransactionIdsAsync(
            string cashAccount,
            DateOnly fromDate,
            DateOnly toDate,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));
    }
}
