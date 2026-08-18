using Pvm.Application.Banking;
using Xunit;

namespace Pvm.Application.Tests.Banking;

public class NedbankOfxParserTests
{
    private readonly NedbankOfxParser _parser = new();

    // The real Netbank "Statement6759" export: 16 STMTTRN rows, one of which is a zero-amount
    // PROVISIONAL STATEMENT marker, and two identical same-day "Trans African" -51.00 lines.
    private const string Sample =
"""
<?xml version="1.0" encoding="US-ASCII"?>
<?OFX OFXHEADER="200" VERSION="202" SECURITY="NONE" OLDFILEUID="NONE" NEWFILEUID="NONE"?>
<OFX>
<BANKMSGSRSV1>
<STMTTRNRS>
<STMTRS>
<CURDEF>ZAR</CURDEF>
<BANKACCTFROM>
<BANKID>NETBANK</BANKID>
<ACCTID>1644294346</ACCTID>
<ACCTTYPE></ACCTTYPE>
</BANKACCTFROM>
<BANKTRANLIST>
<DTSTART>20260807</DTSTART>
<DTEND>20260808</DTEND>
<STMTTRN>
<TRNTYPE>OTHER</TRNTYPE>
<DTPOSTED>20260807</DTPOSTED>
<TRNAMT>-67252.45</TRNAMT>
<FITID>00000675900</FITID>
<CHKNUM>00000675900</CHKNUM>
<NAME>OLD MUTUAL AUG2026</NAME>
</STMTTRN>
<STMTTRN>
<TRNTYPE>OTHER</TRNTYPE>
<DTPOSTED>20260807</DTPOSTED>
<TRNAMT>-31725.82</TRNAMT>
<FITID>00000675901</FITID>
<CHKNUM>00000675901</CHKNUM>
<NAME>COT 2041148567 JUL26</NAME>
</STMTTRN>
<STMTTRN>
<TRNTYPE>OTHER</TRNTYPE>
<DTPOSTED>20260807</DTPOSTED>
<TRNAMT>-16434.11</TRNAMT>
<FITID>00000675902</FITID>
<CHKNUM>00000675902</CHKNUM>
<NAME>OLD MUTUAL AUG2026</NAME>
</STMTTRN>
<STMTTRN>
<TRNTYPE>OTHER</TRNTYPE>
<DTPOSTED>20260807</DTPOSTED>
<TRNAMT>0.00</TRNAMT>
<FITID>00000675908</FITID>
<CHKNUM>00000675908</CHKNUM>
<NAME>PROVISIONAL STATEMENT</NAME>
</STMTTRN>
<STMTTRN>
<TRNTYPE>OTHER</TRNTYPE>
<DTPOSTED>20260808</DTPOSTED>
<TRNAMT>6000.00</TRNAMT>
<FITID>00000675911</FITID>
<CHKNUM>00000675911</CHKNUM>
<NAME>0828592948j opperman</NAME>
</STMTTRN>
<STMTTRN>
<TRNTYPE>OTHER</TRNTYPE>
<DTPOSTED>20260808</DTPOSTED>
<TRNAMT>-51.00</TRNAMT>
<FITID>00000675914</FITID>
<CHKNUM>00000675914</CHKNUM>
<NAME>Trans African 5412815006006813</NAME>
</STMTTRN>
<STMTTRN>
<TRNTYPE>OTHER</TRNTYPE>
<DTPOSTED>20260808</DTPOSTED>
<TRNAMT>-51.00</TRNAMT>
<FITID>00000675915</FITID>
<CHKNUM>00000675915</CHKNUM>
<NAME>Trans African 5412815006006813</NAME>
</STMTTRN>
</BANKTRANLIST>
<LEDGERBAL>
<BALAMT>70336.64</BALAMT>
<DTASOF>20260807</DTASOF>
</LEDGERBAL>
</STMTRS>
</STMTTRNRS>
</BANKMSGSRSV1>
</OFX>
""";

    [Fact]
    public void Parse_drops_zero_amount_provisional_line()
    {
        var statement = _parser.Parse(Sample, "NEDBANK-OPS");

        // 7 STMTTRN in the fixture, one is the zero-amount PROVISIONAL STATEMENT marker.
        Assert.Equal(6, statement.Lines.Count);
        Assert.DoesNotContain(statement.Lines, line => line.Receipt == 0m && line.Disbursement == 0m);
        Assert.DoesNotContain(statement.Lines, line => line.Description == "PROVISIONAL STATEMENT");
    }

    [Fact]
    public void Parse_splits_debit_and_credit()
    {
        var statement = _parser.Parse(Sample, "NEDBANK-OPS");

        var debit = Assert.Single(statement.Lines, line => line.ExtRefNbr == "00000675900");
        Assert.Equal(67252.45m, debit.Disbursement);
        Assert.Equal(0m, debit.Receipt);
        Assert.Equal(new DateOnly(2026, 8, 7), debit.TranDate);
        Assert.Equal("OLD MUTUAL AUG2026", debit.Description);

        var credit = Assert.Single(statement.Lines, line => line.ExtRefNbr == "00000675911");
        Assert.Equal(6000.00m, credit.Receipt);
        Assert.Equal(0m, credit.Disbursement);
    }

    [Fact]
    public void Parse_gives_identical_same_day_lines_distinct_ids()
    {
        var statement = _parser.Parse(Sample, "NEDBANK-OPS");

        var duplicates = statement.Lines
            .Where(line => line.Description == "Trans African 5412815006006813")
            .ToList();

        Assert.Equal(2, duplicates.Count);
        Assert.NotEqual(duplicates[0].ExtTranId, duplicates[1].ExtTranId);
    }

    [Fact]
    public void Parse_is_deterministic_across_runs()
    {
        var first = _parser.Parse(Sample, "NEDBANK-OPS");
        var second = _parser.Parse(Sample, "NEDBANK-OPS");

        Assert.Equal(
            first.Lines.Select(line => line.ExtTranId),
            second.Lines.Select(line => line.ExtTranId));
    }

    [Fact]
    public void Parse_keeps_original_fitid_as_reference()
    {
        var statement = _parser.Parse(Sample, "NEDBANK-OPS");

        Assert.All(statement.Lines, line => Assert.StartsWith("NED-", line.ExtTranId));
        Assert.Contains(statement.Lines, line => line.ExtRefNbr == "00000675914");
    }

    [Fact]
    public void Parse_sets_account_dates_and_consistent_balances()
    {
        var statement = _parser.Parse(Sample, "NEDBANK-OPS");

        Assert.Equal("NEDBANK-OPS", statement.CashAccount);
        Assert.Equal(new DateOnly(2026, 8, 7), statement.StartBalanceDate);
        Assert.Equal(new DateOnly(2026, 8, 8), statement.EndBalanceDate);

        Assert.Equal(70336.64m, statement.EndingBalance);

        // Internal consistency: beginning + net movement == ending.
        var net = statement.Lines.Sum(line => line.Receipt - line.Disbursement);
        Assert.Equal(statement.EndingBalance, statement.BeginningBalance + net);
    }

    [Fact]
    public void Parse_rejects_non_ofx_content()
    {
        Assert.Throws<FormatException>(() => _parser.Parse("not xml at all", "NEDBANK-OPS"));
    }
}
