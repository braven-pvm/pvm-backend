using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pvm.Application.Messaging;
using Pvm.Application.Reporting;
using Pvm.Domain.Reporting;
using Pvm.Infrastructure.PayloadArchive;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Reporting;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Reporting;

public sealed class SellInSyncServiceTests : IAsyncLifetime
{
    private static readonly PostingPeriod March = PostingPeriod.Create(2025, 1);
    private static readonly PostingPeriod April = PostingPeriod.Create(2025, 2);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();
    private readonly string _archiveRoot = Path.Combine(Path.GetTempPath(), $"pvm-sell-in-{Guid.NewGuid():N}");
    private readonly FakeSource _source = new();
    private readonly FakeCustomerReps _customerReps = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        if (Directory.Exists(_archiveRoot))
        {
            Directory.Delete(_archiveRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Lines_load_signed_with_their_financial_month_and_dimensions()
    {
        _source.Set(March,
            SellInRowParserTests.Row(reference: "IN000001", date: "2024-03-04T00:00:00", amount: "100.00", quantity: "2"),
            SellInRowParserTests.Row(type: "CRM", reference: "CM000001", date: "2024-03-20T00:00:00", amount: "30.00", quantity: "1"));

        var result = await RunAsync(March, March);

        Assert.Equal(1, result.PeriodsRead);
        Assert.Equal(2, result.LinesWritten);
        await using var db = CreateDbContext();
        var lines = await db.SellInLines.OrderBy(line => line.ReferenceNumber).ToListAsync();
        Assert.Equal([-30.00m, 100.00m], lines.Select(line => line.Amount));
        Assert.Equal([-1m, 2m], lines.Select(line => line.Quantity));
        Assert.All(lines, line =>
        {
            Assert.Equal(2025, line.FinancialYear);
            Assert.Equal(1, line.FinancialMonth);
            Assert.Equal("202501", line.PostPeriod);
        });
        Assert.Equal("Example Store", (await db.ReportingCustomers.SingleAsync()).Name);
        Assert.Equal("Energy Bars", (await db.ReportingProducts.SingleAsync()).ItemClassName);
        Assert.Equal("Example Rep", (await db.ReportingSalesReps.SingleAsync()).Name);
        Assert.Equal("Finished Goods", (await db.ReportingWarehouseLocations.SingleAsync()).LocationName);
    }

    [Fact]
    public async Task The_financial_month_is_the_posting_period_not_the_document_date()
    {
        // PVM closes March near the 25th. A document of 26 March posts to the next period,
        // and the sales workbook counts it there.
        _source.Set(April, SellInRowParserTests.Row(reference: "IN000001", date: "2024-03-26T00:00:00"));

        await RunAsync(April, April);

        await using var db = CreateDbContext();
        var line = await db.SellInLines.SingleAsync();
        Assert.Equal(new DateOnly(2024, 3, 26), line.DocumentDate);
        Assert.Equal(2025, line.FinancialYear);
        Assert.Equal(2, line.FinancialMonth);
        Assert.Equal("202502", line.PostPeriod);
    }

    [Fact]
    public async Task Each_customer_gets_its_current_rep_from_the_customer_list()
    {
        _source.Set(March,
            SellInRowParserTests.Row(reference: "IN000001", customer: "C0001", rep: "REP01"),
            SellInRowParserTests.Row(reference: "IN000002", customer: "C0002", rep: "REP01"));
        _customerReps.Set(
            new CustomerSalesRep("C0001", "REP02", "Second Rep"),
            new CustomerSalesRep("C0002", null, null),
            new CustomerSalesRep("C0099", "REP03", "Third Rep"));

        await RunAsync(March, March);

        await using var db = CreateDbContext();
        var customers = await db.ReportingCustomers.OrderBy(customer => customer.CustomerId).ToListAsync();
        Assert.Equal(["C0001", "C0002"], customers.Select(customer => customer.CustomerId));
        Assert.Equal("REP02", customers[0].CurrentSalesRepId);
        Assert.Null(customers[1].CurrentSalesRepId);
        // The rep on the line stays as it was at the sale.
        Assert.All(await db.SellInLines.ToListAsync(), line => Assert.Equal("REP01", line.SalesRepId));
        Assert.Equal(["REP01", "REP02"], await db.ReportingSalesReps.OrderBy(rep => rep.SalesRepId).Select(rep => rep.SalesRepId).ToListAsync());
    }

    [Fact]
    public async Task The_report_view_follows_the_workbook_rules_and_lists_the_rest_apart()
    {
        _source.Set(March,
            SellInRowParserTests.Row(reference: "IN000001", amount: "100.00"),
            SellInRowParserTests.Row(type: "CRM", reference: "CM000001", amount: "30.00"),
            SellInRowParserTests.Row(type: "DRM", reference: "DM000001", amount: "5.00"),
            SellInRowParserTests.Row(reference: "IN000002", item: "SAMPLE", itemClass: null, amount: "250.00"),
            SellInRowParserTests.Row(reference: "IN000003", customer: "C0002", rep: null, amount: "10.00"));
        _customerReps.Set(new CustomerSalesRep("C0001", "REP02", "Second Rep"));

        await RunAsync(March, March);

        await using var db = CreateDbContext();
        var report = await db.SellInReportLines.OrderBy(line => line.ReferenceNumber).ToListAsync();
        Assert.Equal(["CM000001", "IN000001", "IN000003"], report.Select(line => line.ReferenceNumber));
        Assert.Equal(80.00m, report.Sum(line => line.Amount));
        var invoice = report.Single(line => line.ReferenceNumber == "IN000001");
        Assert.Equal("REP02", invoice.SalesRepId);
        Assert.Equal("Second Rep", invoice.SalesRepName);
        Assert.Equal("REP01", invoice.SalesRepAtSaleId);
        Assert.Equal("Example Rep", invoice.SalesRepAtSaleName);
        Assert.Equal("Example Retail", invoice.CustomerClassName);
        Assert.Equal("Energy Bars", invoice.ItemClassName);
        // A line with no rep stays in the report.
        Assert.Null(report.Single(line => line.ReferenceNumber == "IN000003").SalesRepId);

        var excluded = await db.SellInExcludedLines.OrderBy(line => line.ReferenceNumber).ToListAsync();
        Assert.Equal(["DM000001", "IN000002"], excluded.Select(line => line.ReferenceNumber));
        Assert.Equal(["debit-memo", "no-item-class"], excluded.Select(line => line.Reason));
    }

    [Fact]
    public async Task A_failed_customer_list_read_fails_the_run_and_keeps_the_lines()
    {
        _source.Set(March, SellInRowParserTests.Row(reference: "IN000001"));
        _customerReps.Fail(new SellInReadException("Acumatica answered 500"));

        await Assert.ThrowsAsync<SellInReadException>(() => RunAsync(March, March));

        await using var db = CreateDbContext();
        Assert.Equal("IN000001", (await db.SellInLines.SingleAsync()).ReferenceNumber);
        var run = await db.IntegrationRuns.SingleAsync();
        Assert.Equal(IntegrationRunStatuses.Failed, run.Status);
        Assert.Equal("sell-in-customer-reps-failed", run.ErrorCode);
    }

    [Fact]
    public async Task A_second_run_replaces_the_period_and_keeps_other_periods()
    {
        _source.Set(March, SellInRowParserTests.Row(reference: "IN000001"), SellInRowParserTests.Row(reference: "IN000002"));
        _source.Set(April, SellInRowParserTests.Row(reference: "IN000100", date: "2024-04-02T00:00:00"));
        await RunAsync(March, April);

        // IN000002 was deleted in Acumatica, and IN000003 is new.
        _source.Set(March, SellInRowParserTests.Row(reference: "IN000001"), SellInRowParserTests.Row(reference: "IN000003"));
        await RunAsync(March, March);

        await using var db = CreateDbContext();
        var references = await db.SellInLines.OrderBy(line => line.ReferenceNumber).Select(line => line.ReferenceNumber).ToListAsync();
        Assert.Equal(["IN000001", "IN000003", "IN000100"], references);
    }

    [Fact]
    public async Task Two_runs_of_the_same_periods_both_succeed()
    {
        // The nightly load reads the same periods each night of a month.
        _source.Set(March, SellInRowParserTests.Row());
        await RunAsync(March, April);
        await RunAsync(March, April);

        await using var db = CreateDbContext();
        var runs = await db.IntegrationRuns.ToListAsync();
        Assert.Equal(2, runs.Count);
        Assert.All(runs, run => Assert.Equal(IntegrationRunStatuses.Succeeded, run.Status));
        Assert.Equal(2, runs.Select(run => run.ScheduleKey).Distinct().Count());
    }

    [Fact]
    public async Task Each_run_is_logged_and_each_period_is_archived_raw()
    {
        _source.Set(March, SellInRowParserTests.Row(), SellInRowParserTests.Row(type: "PMT", reference: "PM000001"));

        var result = await RunAsync(March, March);

        await using var db = CreateDbContext();
        var run = await db.IntegrationRuns.SingleAsync();
        Assert.Equal(result.RunId, run.Id);
        Assert.Equal(SellInSyncService.RunType, run.RunType);
        Assert.Equal(IntegrationRunStatuses.Succeeded, run.Status);
        Assert.Equal(2, run.ReceivedCount);
        Assert.Equal(1, run.CreatedCount);
        Assert.Equal(1, run.SkippedCount);
        var archived = Directory.GetFiles(_archiveRoot, "*.json", SearchOption.AllDirectories);
        Assert.Single(archived);
        Assert.Contains($"{result.RunId:N}", Path.GetFileName(archived[0]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_period_stops_the_run_keeps_earlier_periods_and_logs_the_failure()
    {
        _source.Set(March, SellInRowParserTests.Row(reference: "IN000001"));
        _source.Fail(April, new SellInReadException("Acumatica answered 500"));

        await Assert.ThrowsAsync<SellInReadException>(() => RunAsync(March, April));

        await using var db = CreateDbContext();
        Assert.Equal("IN000001", (await db.SellInLines.SingleAsync()).ReferenceNumber);
        var run = await db.IntegrationRuns.SingleAsync();
        Assert.Equal(IntegrationRunStatuses.Failed, run.Status);
        Assert.Equal("sell-in-read-failed", run.ErrorCode);
        Assert.Contains("022025", run.ErrorSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_line_returned_twice_fails_the_period_and_writes_nothing_for_it()
    {
        _source.Set(March, SellInRowParserTests.Row(reference: "IN000001"), SellInRowParserTests.Row(reference: "IN000001"));

        var exception = await Assert.ThrowsAsync<SellInReadException>(() => RunAsync(March, March));

        Assert.Contains("2 times", exception.Message, StringComparison.Ordinal);
        await using var db = CreateDbContext();
        Assert.Empty(await db.SellInLines.ToListAsync());
    }

    [Fact]
    public async Task A_switched_off_load_refuses_to_run()
    {
        var service = CreateService(enabled: false, CreateDbContext());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RunAsync(March, March, IntegrationRunTriggers.Manual, "test", CancellationToken.None));
    }

    [Theory]
    [InlineData("2026-11-12", 2, 1, null, "072027", "102027")]
    [InlineData("2026-11-12", 0, 0, null, "092027", "092027")]
    [InlineData("2026-03-02", 2, 1, "012027", "012027", "022027")]
    public void Window_reads_back_and_ahead_but_never_before_the_floor(
        string today, int back, int ahead, string? floor, string expectedFrom, string expectedTo)
    {
        var options = new SellInSyncOptions { PeriodsBack = back, PeriodsAhead = ahead, FirstPeriod = floor };

        var (from, to) = SellInSyncService.ResolveWindow(DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture), options);

        Assert.Equal(expectedFrom, from.DisplayId);
        Assert.Equal(expectedTo, to.DisplayId);
    }

    private async Task<SellInSyncResult> RunAsync(PostingPeriod from, PostingPeriod to)
    {
        await using var db = CreateDbContext();
        return await CreateService(enabled: true, db).RunAsync(from, to, IntegrationRunTriggers.Manual, "test", CancellationToken.None);
    }

    private SellInSyncService CreateService(bool enabled, PvmDbContext db) => new(
        db,
        _source,
        _customerReps,
        Options.Create(new SellInSyncOptions { Enabled = enabled }),
        new ConfigurationBuilder().Build(),
        TimeProvider.System,
        NullLogger<SellInSyncService>.Instance,
        new FileSystemPayloadArchive(_archiveRoot, "payloads"));

    private PvmDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<PvmDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private sealed class FakeCustomerReps : ICustomerSalesRepSource
    {
        private IReadOnlyList<CustomerSalesRep> _customers = [];
        private Exception? _failure;

        public void Set(params CustomerSalesRep[] customers) => _customers = customers;

        public void Fail(Exception exception) => _failure = exception;

        public Task<IReadOnlyList<CustomerSalesRep>> ReadAllAsync(CancellationToken cancellationToken) =>
            _failure is null ? Task.FromResult(_customers) : throw _failure;
    }

    private sealed class FakeSource : ISellInSource
    {
        private readonly Dictionary<PostingPeriod, string> _responses = [];
        private readonly Dictionary<PostingPeriod, Exception> _failures = [];

        public void Set(PostingPeriod period, params string[] rows) => _responses[period] = SellInRowParserTests.Response(rows);

        public void Fail(PostingPeriod period, Exception exception) => _failures[period] = exception;

        public Task<SellInPeriodRead> ReadPeriodAsync(PostingPeriod period, CancellationToken cancellationToken)
        {
            if (_failures.TryGetValue(period, out var failure))
            {
                throw failure;
            }

            var json = _responses.GetValueOrDefault(period, SellInRowParserTests.Response());
            var (rows, skipped) = SellInRowParser.Parse(json);
            return Task.FromResult(new SellInPeriodRead(period, json, rows, skipped));
        }
    }
}
