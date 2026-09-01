using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Pvm.Application.Banking;

/// <summary>
/// Parses a Nedbank (Netbank) OFX 2.0 statement export into the canonical
/// <see cref="BankStatementImport"/> that the Acumatica <c>PVMBankFeed</c> endpoint accepts.
///
/// Nedbank's native OFX cannot be imported as-is: its <c>FITID</c> is positional
/// (statement-number + line-index), so it shifts between provisional and final downloads and
/// causes Acumatica — which de-duplicates on that id — to re-import duplicates. This parser
/// "renumbers" every line onto a stable, content-derived id and drops zero-amount noise lines
/// (for example the <c>PROVISIONAL STATEMENT</c> marker). The original <c>FITID</c> is kept in
/// <see cref="BankStatementLine.ExtRefNbr"/> for audit.
/// </summary>
public sealed class NedbankOfxParser
{
    /// <summary>
    /// Parses <paramref name="ofxContent"/> and targets the Acumatica cash account
    /// <paramref name="cashAccount"/>. Throws <see cref="FormatException"/> when the content is
    /// not well-formed OFX 2.0 XML.
    /// </summary>
    public BankStatementImport Parse(string ofxContent, string cashAccount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ofxContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(cashAccount);

        XDocument document;
        try
        {
            document = XDocument.Parse(ofxContent);
        }
        catch (System.Xml.XmlException exception)
        {
            throw new FormatException(
                "Nedbank statement is not well-formed OFX 2.0 XML. Only the XML (OFX 2.x) export is supported.",
                exception);
        }

        var root = document.Root
            ?? throw new FormatException("Nedbank OFX has no root element.");

        var accountId = FirstValue(root, "ACCTID") ?? "NEDBANK";
        var tranList = Descendant(root, "BANKTRANLIST");
        var startDate = ParseOfxDate(Value(tranList, "DTSTART"));
        var endDate = ParseOfxDate(Value(tranList, "DTEND"));

        var lines = BuildLines(root, accountId);

        var (beginning, ending) = ResolveBalances(root, lines);
        var statementDate = endDate ?? lines.LastOrDefault()?.TranDate ?? startDate ?? DateOnly.MinValue;

        return new BankStatementImport(
            CashAccount: cashAccount,
            StatementDate: statementDate,
            StartBalanceDate: startDate ?? statementDate,
            EndBalanceDate: statementDate,
            BeginningBalance: beginning,
            EndingBalance: ending,
            Lines: lines);
    }

    private static List<BankStatementLine> BuildLines(XElement root, string accountId)
    {
        var lines = new List<BankStatementLine>();

        // Occurrence counter so two otherwise-identical same-day lines get distinct ids.
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var stmtTrn in root.Descendants().Where(element => LocalName(element) == "STMTTRN"))
        {
            var amountText = Value(stmtTrn, "TRNAMT");
            if (!decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                continue;
            }

            // Drop zero-amount noise (for example the PROVISIONAL STATEMENT marker): it carries
            // no reconciliation value and Acumatica rejects a line with no receipt or disbursement.
            if (amount == 0m)
            {
                continue;
            }

            var postedDate = ParseOfxDate(Value(stmtTrn, "DTPOSTED")) ?? DateOnly.MinValue;
            var description = Value(stmtTrn, "NAME") ?? string.Empty;
            var originalFitId = Value(stmtTrn, "FITID");

            var contentKey = string.Join(
                '|',
                accountId,
                postedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                amount.ToString(CultureInfo.InvariantCulture),
                description);
            var occurrence = occurrences.TryGetValue(contentKey, out var count) ? count : 0;
            occurrences[contentKey] = occurrence + 1;

            lines.Add(new BankStatementLine(
                ExtTranId: StableId(contentKey, occurrence),
                TranDate: postedDate,
                Description: description,
                Receipt: amount > 0m ? amount : 0m,
                Disbursement: amount < 0m ? -amount : 0m,
                ExtRefNbr: NullIfBlank(originalFitId)));
        }

        return lines;
    }

    // Stable, content-derived id that replaces Nedbank's positional FITID. The occurrence index
    // keeps duplicate same-day lines distinct while staying identical across re-downloads of the
    // same statement, so Acumatica's de-duplication is idempotent.
    private static string StableId(string contentKey, int occurrence)
    {
        var key = contentKey + "|" + occurrence.ToString(CultureInfo.InvariantCulture);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return "NED-" + Convert.ToHexString(hash)[..28];
    }

    // Prefer the bank-provided ledger balance as the ending balance and derive the beginning
    // from the net movement, so the statement is internally consistent for Acumatica.
    // NOTE: OFX LEDGERBAL carries its own DTASOF; on a provisional export that date can lag the
    // statement end. Confirm against a FINAL Nedbank export before go-live.
    private static (decimal Beginning, decimal Ending) ResolveBalances(
        XElement root,
        IReadOnlyList<BankStatementLine> lines)
    {
        var net = lines.Sum(line => line.Receipt - line.Disbursement);

        var ledger = Descendant(root, "LEDGERBAL");
        if (ledger is not null
            && decimal.TryParse(Value(ledger, "BALAMT"), NumberStyles.Number, CultureInfo.InvariantCulture, out var ending))
        {
            return (ending - net, ending);
        }

        return (0m, net);
    }

    private static DateOnly? ParseOfxDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 8)
        {
            return null;
        }

        // OFX dates are YYYYMMDD, optionally followed by HHMMSS and a timezone. Take the date.
        return DateOnly.TryParseExact(
            value[..8],
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;
    }

    private static string LocalName(XElement element) => element.Name.LocalName;

    private static XElement? Descendant(XElement? parent, string localName) =>
        parent?.Descendants().FirstOrDefault(element => LocalName(element) == localName);

    private static string? Value(XElement? parent, string localName)
    {
        var element = Descendant(parent, localName);
        return element is null ? null : NullIfBlank(element.Value);
    }

    private static string? FirstValue(XElement root, string localName) =>
        Value(root, localName);

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
