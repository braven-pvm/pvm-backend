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
        var statement = parser.Parse(ofxContent, RequireCashAccount());
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
        var cashAccount = RequireCashAccount();
        var statement = parser.Parse(ofxContent, cashAccount);

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

    private string RequireCashAccount()
    {
        if (string.IsNullOrWhiteSpace(_options.CashAccount))
        {
            throw new InvalidOperationException(
                "Nedbank CashAccount (Acumatica target) is required to import a statement.");
        }

        return _options.CashAccount;
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
