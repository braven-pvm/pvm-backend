using Microsoft.Extensions.Options;
using Pvm.Application.Banking;

namespace Pvm.Infrastructure.Nedbank;

/// <summary>
/// Imports one manually-downloaded Nedbank OFX statement into Acumatica: parse and renumber the
/// file, then PUT it to the <c>PVMBankFeed</c> endpoint. The write path filters lines Acumatica
/// already holds, so re-uploading an overlapping export imports no duplicates. Skips the PUT
/// when the file has no importable lines.
///
/// <see cref="PreviewAsync"/> runs the same parse and the same duplicate lookup but writes
/// nothing, so a person can check the figures before committing them.
/// </summary>
public sealed class NedbankStatementImportService(
    NedbankOfxParser parser,
    IAcumaticaBankStatementClient acumaticaClient,
    IOptions<NedbankOptions> options)
{
    // A statement that starts long after the last import is usually a missed download rather
    // than a quiet week, but listing every day of a months-long hole helps nobody.
    private const int MaxUncoveredDatesListed = 14;

    private readonly NedbankOptions _options = options.Value;

    public async Task<NedbankImportResult> ImportAsync(
        string ofxContent,
        CancellationToken cancellationToken = default)
    {
        var statement = ParseAndRoute(ofxContent);
        if (statement.Lines.Count == 0)
        {
            return new NedbankImportResult(0, null, statement.StartBalanceDate, statement.EndBalanceDate);
        }

        var import = await acumaticaClient.ImportAsync(statement, cancellationToken);
        return new NedbankImportResult(
            import.LineCount,
            string.IsNullOrEmpty(import.ReferenceNbr) ? null : import.ReferenceNbr,
            statement.StartBalanceDate,
            statement.EndBalanceDate,
            statement.BeginningBalance,
            statement.EndingBalance,
            statement.Lines.Count - import.LineCount);
    }

    /// <summary>
    /// Works out what the file would do, without writing to Acumatica.
    /// </summary>
    public async Task<BankStatementPreview> PreviewAsync(
        string ofxContent,
        CancellationToken cancellationToken = default)
    {
        var statement = ParseAndRoute(ofxContent);
        var cashAccount = statement.CashAccount;

        var latest = await acumaticaClient.GetLatestStatementAsync(cashAccount, cancellationToken);

        var imported = statement.Lines.Count == 0
            ? (IReadOnlySet<string>)new HashSet<string>(StringComparer.Ordinal)
            : await acumaticaClient.GetImportedTransactionIdsAsync(
                cashAccount,
                statement.Lines.Min(line => line.TranDate),
                statement.Lines.Max(line => line.TranDate),
                cancellationToken);

        var lines = statement.Lines
            .Select(line => new BankStatementPreviewLine(
                line.TranDate,
                line.Description,
                line.Receipt,
                line.Disbursement,
                imported.Contains(line.ExtTranId)))
            .ToList();

        var importedThrough = latest?.EndBalanceDate;

        return new BankStatementPreview(
            CashAccount: cashAccount,
            SourceAccountNumber: statement.SourceAccountNumber,
            PeriodStart: statement.StartBalanceDate,
            PeriodEnd: statement.EndBalanceDate,
            MoneyIn: lines.Sum(line => line.Receipt),
            MoneyOut: lines.Sum(line => line.Disbursement),
            OpeningBalance: statement.BeginningBalance,
            ClosingBalance: statement.EndingBalance,
            ImportedThrough: importedThrough,
            ImportedThroughBalance: latest?.EndingBalance,
            BalancesCarryForward: latest is not null && latest.EndingBalance == statement.BeginningBalance,
            OverlapsImportedPeriod: importedThrough is not null && statement.StartBalanceDate <= importedThrough,
            AlreadyImportedCount: lines.Count(line => line.AlreadyImported),
            NewCount: lines.Count(line => !line.AlreadyImported),
            UncoveredDates: UncoveredDates(importedThrough, statement.StartBalanceDate),
            Lines: lines);
    }

    // The days between the last import and this statement that no statement covers. A weekend
    // gap is normal, so the caller shows the day names and lets the person judge.
    private static IReadOnlyList<DateOnly> UncoveredDates(DateOnly? importedThrough, DateOnly periodStart)
    {
        if (importedThrough is null || periodStart <= importedThrough)
        {
            return [];
        }

        var dates = new List<DateOnly>();
        for (var date = importedThrough.Value.AddDays(1);
             date < periodStart && dates.Count < MaxUncoveredDatesListed;
             date = date.AddDays(1))
        {
            dates.Add(date);
        }

        return dates;
    }

    // The file decides where it lands. Parse it, read the account it is for, then look that
    // account up. Nothing is written until the lookup succeeds.
    private BankStatementImport ParseAndRoute(string ofxContent)
    {
        var parsed = parser.Parse(ofxContent);
        return parsed with { CashAccount = ResolveCashAccount(parsed.SourceAccountNumber) };
    }

    private string ResolveCashAccount(string? sourceAccountNumber)
    {
        if (_options.Accounts.Count == 0)
        {
            throw new InvalidOperationException(
                "No Nedbank accounts are configured. Map each bank account number to its "
                + "Acumatica cash account under Nedbank:Accounts.");
        }

        if (string.IsNullOrWhiteSpace(sourceAccountNumber))
        {
            throw new UnknownBankAccountException(
                "This statement does not name a bank account, so it cannot be matched to an "
                + "Acumatica cash account. Export the statement again from Nedbank.");
        }

        var account = sourceAccountNumber.Trim();
        foreach (var (number, cashAccount) in _options.Accounts)
        {
            if (string.Equals(number.Trim(), account, StringComparison.OrdinalIgnoreCase))
            {
                return cashAccount;
            }
        }

        throw new UnknownBankAccountException(
            $"This statement is for Nedbank account {account}, which is not mapped to an "
            + "Acumatica cash account. Known accounts: "
            + string.Join(", ", _options.Accounts.Select(pair => $"{pair.Key} ({pair.Value})"))
            + ". Nothing was imported.");
    }
}

/// <summary>Outcome of a Nedbank statement import.</summary>
public sealed record NedbankImportResult(
    int LinesImported,
    string? StatementReference,
    DateOnly? PeriodStart = null,
    DateOnly? PeriodEnd = null,
    decimal OpeningBalance = 0m,
    decimal ClosingBalance = 0m,
    int AlreadyImportedCount = 0);
