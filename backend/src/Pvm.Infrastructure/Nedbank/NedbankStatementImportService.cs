using Microsoft.Extensions.Options;
using Pvm.Application.Banking;

namespace Pvm.Infrastructure.Nedbank;

/// <summary>
/// Imports one manually-downloaded Nedbank OFX statement into Acumatica: parse and renumber the
/// file, then PUT it to the <c>PVMBankFeed</c> endpoint. Acumatica de-duplicates on
/// Ext. Tran. ID, so re-uploading an overlapping export imports no duplicate lines. Skips the
/// PUT when the file has no importable lines.
/// </summary>
public sealed class NedbankStatementImportService(
    NedbankOfxParser parser,
    IAcumaticaBankStatementClient acumaticaClient,
    IOptions<NedbankOptions> options)
{
    private readonly NedbankOptions _options = options.Value;

    public async Task<NedbankImportResult> ImportAsync(
        string ofxContent,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.CashAccount))
        {
            throw new InvalidOperationException(
                "Nedbank CashAccount (Acumatica target) is required to import a statement.");
        }

        var statement = parser.Parse(ofxContent, _options.CashAccount);
        if (statement.Lines.Count == 0)
        {
            return new NedbankImportResult(0, null);
        }

        var import = await acumaticaClient.ImportAsync(statement, cancellationToken);
        return new NedbankImportResult(import.LineCount, import.ReferenceNbr);
    }
}

/// <summary>Outcome of a Nedbank statement import.</summary>
public sealed record NedbankImportResult(int LinesImported, string? StatementReference);
