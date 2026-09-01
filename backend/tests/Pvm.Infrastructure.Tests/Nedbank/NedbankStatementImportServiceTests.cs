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
    }
}
