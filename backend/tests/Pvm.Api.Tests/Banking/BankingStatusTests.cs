using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
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

    [Fact]
    public async Task Accounts_are_read_at_the_same_time_and_keep_their_order()
    {
        // Each read takes a moment. Read one after the other, never more than one is in flight.
        var client = new StubClient { Delay = TimeSpan.FromMilliseconds(200) };

        var read = await BankImportEndpoints.ReadStatusAsync(client, Nedbank, Investec, CancellationToken.None);

        Assert.True(read.Complete);
        Assert.Equal(
            ["ECNEDBANK", "PVMNEDBANK", "WCNEDBANK", "INVESTEC"],
            read.Response.Accounts.Select(account => account.CashAccount));
        Assert.All(read.Response.Accounts, account => Assert.True(account.Available));
        Assert.Equal(4, client.Calls);
        Assert.Equal(BankImportEndpoints.MaxConcurrentAccountReads, client.MaxInFlight);
    }

    [Fact]
    public async Task A_failed_account_read_marks_only_that_account_unavailable()
    {
        var client = new StubClient { Failing = { "PVMNEDBANK" } };

        var read = await BankImportEndpoints.ReadStatusAsync(client, Nedbank, Investec, CancellationToken.None);

        Assert.False(read.Complete);
        var byAccount = read.Response.Accounts.ToDictionary(account => account.CashAccount);
        Assert.False(byAccount["PVMNEDBANK"].Available);
        Assert.True(byAccount["ECNEDBANK"].Available);
        Assert.True(byAccount["WCNEDBANK"].Available);
        Assert.True(byAccount["INVESTEC"].Available);
        Assert.Equal(new DateOnly(2026, 11, 10), byAccount["ECNEDBANK"].ImportedThrough);
    }

    [Fact]
    public async Task The_status_is_read_once_and_then_served_from_the_cache()
    {
        var cache = NewCache();
        var client = new StubClient();

        var first = await cache.GetOrReadAsync(Read(client), CancellationToken.None);
        var second = await cache.GetOrReadAsync(Read(client), CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(4, client.Calls);
    }

    [Fact]
    public async Task A_status_with_a_failed_read_is_not_cached()
    {
        var cache = NewCache();
        var client = new StubClient { Failing = { "INVESTEC" } };

        await cache.GetOrReadAsync(Read(client), CancellationToken.None);
        client.Failing.Clear();
        var second = await cache.GetOrReadAsync(Read(client), CancellationToken.None);

        Assert.Equal(8, client.Calls);
        Assert.All(second.Accounts, account => Assert.True(account.Available));
    }

    [Fact]
    public async Task A_read_that_started_before_an_invalidation_is_not_cached()
    {
        var cache = NewCache();
        var client = new StubClient();

        await cache.GetOrReadAsync(
            async token =>
            {
                var result = await Read(client)(token);
                cache.Invalidate();
                return result;
            },
            CancellationToken.None);
        await cache.GetOrReadAsync(Read(client), CancellationToken.None);

        Assert.Equal(8, client.Calls);
    }

    [Fact]
    public async Task A_successful_nedbank_import_clears_the_cached_status()
    {
        var cache = NewCache();
        var client = new StubClient();
        await cache.GetOrReadAsync(Read(client), CancellationToken.None);
        var importer = new NedbankStatementImportService(
            new NedbankOfxParser(),
            client,
            Options.Create(new NedbankOptions { Accounts = { ["1644294346"] = "PVMNEDBANK" } }));

        var result = await BankImportEndpoints.ImportNedbankAsync(
            OfxFile(), importer, cache, CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, (result as IStatusCodeHttpResult)?.StatusCode);
        Assert.Equal(1, client.Imports);
        await cache.GetOrReadAsync(Read(client), CancellationToken.None);
        Assert.Equal(8, client.Calls);
    }

    [Fact]
    public async Task A_refused_nedbank_import_keeps_the_cached_status()
    {
        var cache = NewCache();
        var client = new StubClient();
        await cache.GetOrReadAsync(Read(client), CancellationToken.None);
        var importer = new NedbankStatementImportService(
            new NedbankOfxParser(),
            client,
            Options.Create(new NedbankOptions { Accounts = { ["9999999999"] = "OTHER" } }));

        var result = await BankImportEndpoints.ImportNedbankAsync(
            OfxFile(), importer, cache, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, (result as IStatusCodeHttpResult)?.StatusCode);
        await cache.GetOrReadAsync(Read(client), CancellationToken.None);
        Assert.Equal(4, client.Calls);
    }

    private static BankingStatusCache NewCache()
        => new(new MemoryCache(new MemoryCacheOptions()));

    private static Func<CancellationToken, Task<BankingStatusRead>> Read(StubClient client)
        => token => BankImportEndpoints.ReadStatusAsync(client, Nedbank, Investec, token);

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

    private sealed class StubClient : IAcumaticaBankStatementClient
    {
        private int _calls;
        private int _inFlight;
        private int _maxInFlight;

        public TimeSpan Delay { get; init; } = TimeSpan.Zero;

        public HashSet<string> Failing { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int Calls => Volatile.Read(ref _calls);

        public int MaxInFlight => Volatile.Read(ref _maxInFlight);

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

        public async Task<IReadOnlyList<BankStatementSummary>> GetRecentStatementsAsync(
            string cashAccount,
            int count,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            var inFlight = Interlocked.Increment(ref _inFlight);
            InterlockedMax(ref _maxInFlight, inFlight);
            try
            {
                await Task.Delay(Delay, cancellationToken);

                if (Failing.Contains(cashAccount))
                {
                    throw new HttpRequestException("Acumatica is unreachable.");
                }

                return
                [
                    new BankStatementSummary("001", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 10), 0m, 10m, 2)
                ];
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        public Task<IReadOnlySet<string>> GetImportedTransactionIdsAsync(
            string cashAccount,
            DateOnly fromDate,
            DateOnly toDate,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            while ((current = Volatile.Read(ref target)) < value
                   && Interlocked.CompareExchange(ref target, value, current) != current)
            {
            }
        }
    }
}
