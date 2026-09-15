using Microsoft.Extensions.Options;
using Pvm.Application.Banking;
using Pvm.Infrastructure.Nedbank;
using Xunit;

namespace Pvm.Infrastructure.Tests.Nedbank;

public sealed class NedbankStatementImportServiceTests
{
    private const string Sample =
"""
<?xml version="1.0" encoding="US-ASCII"?>
<OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS>
<BANKACCTFROM><ACCTID>1644294346</ACCTID></BANKACCTFROM>
<BANKTRANLIST><DTSTART>20260807</DTSTART><DTEND>20260807</DTEND>
<STMTTRN><DTPOSTED>20260807</DTPOSTED><TRNAMT>-100.00</TRNAMT><FITID>00000675900</FITID><NAME>DEBIT ONE</NAME></STMTTRN>
<STMTTRN><DTPOSTED>20260807</DTPOSTED><TRNAMT>250.00</TRNAMT><FITID>00000675901</FITID><NAME>CREDIT ONE</NAME></STMTTRN>
</BANKTRANLIST><LEDGERBAL><BALAMT>150.00</BALAMT><DTASOF>20260807</DTASOF></LEDGERBAL>
</STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>
""";

    [Fact]
    public async Task ImportAsync_parses_and_imports()
    {
        var acumatica = new StubClient();
        var service = new NedbankStatementImportService(
            new NedbankOfxParser(),
            acumatica,
            Options.Create(new NedbankOptions { CashAccount = "NEDBANK-OPS" }));

        var result = await service.ImportAsync(Sample, CancellationToken.None);

        Assert.Equal(2, result.LinesImported);
        Assert.Equal("STMT-9", result.StatementReference);
        Assert.NotNull(acumatica.Last);
        Assert.Equal("NEDBANK-OPS", acumatica.Last!.CashAccount);
        Assert.Equal(2, acumatica.Last.Lines.Count);
    }

    [Fact]
    public async Task ImportAsync_requires_cash_account()
    {
        var service = new NedbankStatementImportService(
            new NedbankOfxParser(),
            new StubClient(),
            Options.Create(new NedbankOptions()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ImportAsync(Sample, CancellationToken.None));
    }

    [Fact]
    public async Task PreviewAsync_reports_the_file_and_writes_nothing()
    {
        var acumatica = new StubClient();
        var service = NewService(acumatica);

        var preview = await service.PreviewAsync(Sample, CancellationToken.None);

        Assert.Null(acumatica.Last);
        Assert.Equal(2, preview.TransactionCount);
        Assert.Equal(2, preview.NewCount);
        Assert.Equal(0, preview.AlreadyImportedCount);
        Assert.Equal(250m, preview.MoneyIn);
        Assert.Equal(100m, preview.MoneyOut);
        Assert.Equal(150m, preview.ClosingBalance);
        Assert.Equal("1644294346", preview.SourceAccountNumber);
        Assert.Equal(new DateOnly(2026, 8, 7), preview.PeriodStart);
    }

    [Fact]
    public async Task PreviewAsync_marks_lines_Acumatica_already_holds()
    {
        var acumatica = new StubClient();
        var parsed = new NedbankOfxParser().Parse(Sample, "NEDBANK-OPS");
        acumatica.Imported.Add(parsed.Lines[0].ExtTranId);

        var preview = await NewService(acumatica).PreviewAsync(Sample, CancellationToken.None);

        Assert.Equal(1, preview.AlreadyImportedCount);
        Assert.Equal(1, preview.NewCount);
        Assert.True(preview.Lines[0].AlreadyImported);
        Assert.False(preview.Lines[1].AlreadyImported);
        Assert.False(preview.NothingToImport);
    }

    [Fact]
    public async Task PreviewAsync_reports_nothing_to_import_when_every_line_exists()
    {
        var acumatica = new StubClient();
        var parsed = new NedbankOfxParser().Parse(Sample, "NEDBANK-OPS");
        foreach (var line in parsed.Lines)
        {
            acumatica.Imported.Add(line.ExtTranId);
        }

        var preview = await NewService(acumatica).PreviewAsync(Sample, CancellationToken.None);

        Assert.True(preview.NothingToImport);
        Assert.Equal(0, preview.NewCount);
    }

    [Fact]
    public async Task PreviewAsync_flags_a_period_that_is_already_imported()
    {
        var acumatica = new StubClient
        {
            Latest = new BankStatementSummary(
                "001254", new DateOnly(2026, 8, 6), new DateOnly(2026, 8, 7), 0m, 150m, 3),
        };

        var preview = await NewService(acumatica).PreviewAsync(Sample, CancellationToken.None);

        Assert.True(preview.OverlapsImportedPeriod);
        Assert.Equal(new DateOnly(2026, 8, 7), preview.ImportedThrough);
        Assert.Empty(preview.UncoveredDates);
    }

    [Fact]
    public async Task PreviewAsync_confirms_the_balances_carry_forward()
    {
        var acumatica = new StubClient
        {
            // The sample's opening balance is 0, so a prior statement closing at 0 continues it.
            Latest = new BankStatementSummary(
                "001253", new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 4), 0m, 0m, 2),
        };

        var preview = await NewService(acumatica).PreviewAsync(Sample, CancellationToken.None);

        Assert.True(preview.BalancesCarryForward);
        Assert.False(preview.OverlapsImportedPeriod);
    }

    [Fact]
    public async Task PreviewAsync_lists_the_days_no_statement_covers()
    {
        var acumatica = new StubClient
        {
            Latest = new BankStatementSummary(
                "001253", new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 4), 0m, 99m, 2),
        };

        var preview = await NewService(acumatica).PreviewAsync(Sample, CancellationToken.None);

        Assert.Equal(
            [new DateOnly(2026, 8, 5), new DateOnly(2026, 8, 6)],
            preview.UncoveredDates);
        Assert.False(preview.BalancesCarryForward);
    }

    private static NedbankStatementImportService NewService(StubClient acumatica)
        => new(
            new NedbankOfxParser(),
            acumatica,
            Options.Create(new NedbankOptions { CashAccount = "NEDBANK-OPS" }));

    private sealed class StubClient : IAcumaticaBankStatementClient
    {
        public BankStatementImport? Last { get; private set; }

        public Task<BankStatementImportResult> ImportAsync(
            BankStatementImport statement,
            CancellationToken cancellationToken = default)
        {
            Last = statement;
            return Task.FromResult(new BankStatementImportResult("STMT-9", statement.Lines.Count));
        }

        public BankStatementSummary? Latest { get; set; }

        public HashSet<string> Imported { get; } = new(StringComparer.Ordinal);

        public Task<BankStatementSummary?> GetLatestStatementAsync(
            string cashAccount,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Latest);

        public Task<IReadOnlyList<BankStatementSummary>> GetRecentStatementsAsync(
            string cashAccount,
            int count,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BankStatementSummary>>(Latest is null ? [] : [Latest]);

        public Task<IReadOnlySet<string>> GetImportedTransactionIdsAsync(
            string cashAccount,
            DateOnly fromDate,
            DateOnly toDate,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlySet<string>>(Imported);
    }
}
