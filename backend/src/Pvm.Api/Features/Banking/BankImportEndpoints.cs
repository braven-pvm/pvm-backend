using Microsoft.Extensions.Options;
using Pvm.Application.Banking;
using Pvm.Infrastructure.Investec;
using Pvm.Infrastructure.Nedbank;

namespace Pvm.Api.Features.Banking;

/// <summary>
/// Bank-statement import endpoints:
/// <list type="bullet">
///   <item><c>GET /api/banking/status</c> — how far each cash account is imported, and its
///   recent statements. The workbench shows this before anyone uploads, so the same days are
///   never imported twice.</item>
///   <item><c>POST /api/banking/import/nedbank/preview</c> — parse an uploaded Nedbank OFX file
///   and report what it would do. Writes nothing.</item>
///   <item><c>POST /api/banking/import/nedbank</c> — upload a Nedbank OFX file; it is parsed,
///   renumbered, and pushed into Acumatica (PVMBankFeed).</item>
///   <item><c>POST /api/banking/investec/refresh</c> — pull Investec transactions for a date
///   window and import them into Acumatica.</item>
/// </list>
/// </summary>
public static class BankImportEndpoints
{
    private const int RecentStatementCount = 5;

    public static IEndpointRouteBuilder MapBankImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/banking");

        group.MapGet("/status", GetStatusAsync)
            .RequireAuthorization();

        group.MapPost("/import/nedbank/preview", PreviewNedbankAsync)
            .RequireAuthorization("Invoices.Write")
            .DisableAntiforgery();

        group.MapPost("/import/nedbank", ImportNedbankAsync)
            .RequireAuthorization("Invoices.Write")
            .DisableAntiforgery();

        group.MapPost("/investec/refresh", RefreshInvestecAsync)
            .RequireAuthorization("Invoices.Write");

        return app;
    }

    private static async Task<IResult> GetStatusAsync(
        IAcumaticaBankStatementClient acumaticaClient,
        IOptions<NedbankOptions> nedbankOptions,
        IOptions<InvestecOptions> investecOptions,
        CancellationToken cancellationToken)
    {
        var accounts = new List<BankAccountStatus>
        {
            await ReadAccountAsync(
                acumaticaClient, "Nedbank", nedbankOptions.Value.CashAccount, "Upload", cancellationToken),
            await ReadAccountAsync(
                acumaticaClient, "Investec", investecOptions.Value.CashAccount, "Automatic", cancellationToken),
        };

        return Results.Ok(new BankingStatusResponse(accounts));
    }

    private static async Task<BankAccountStatus> ReadAccountAsync(
        IAcumaticaBankStatementClient acumaticaClient,
        string bank,
        string? cashAccount,
        string mode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cashAccount))
        {
            return new BankAccountStatus(bank, string.Empty, mode, false, null, null, []);
        }

        try
        {
            var recent = await acumaticaClient.GetRecentStatementsAsync(
                cashAccount, RecentStatementCount, cancellationToken);
            var latest = recent.Count == 0 ? null : recent[0];

            return new BankAccountStatus(
                bank,
                cashAccount,
                mode,
                true,
                latest?.EndBalanceDate,
                latest?.EndingBalance,
                recent);
        }
        catch (InvalidOperationException)
        {
            // Acumatica is unreachable or misconfigured. The page still renders, and says so,
            // rather than failing whole. The import path reports the real error if it is tried.
            return new BankAccountStatus(bank, cashAccount, mode, false, null, null, []);
        }
        catch (HttpRequestException)
        {
            return new BankAccountStatus(bank, cashAccount, mode, false, null, null, []);
        }
    }

    private static async Task<IResult> PreviewNedbankAsync(
        IFormFile file,
        NedbankStatementImportService importer,
        CancellationToken cancellationToken)
        => await ReadAndRunAsync(
            file,
            async ofx => Results.Ok(await importer.PreviewAsync(ofx, cancellationToken)),
            cancellationToken);

    private static async Task<IResult> ImportNedbankAsync(
        IFormFile file,
        NedbankStatementImportService importer,
        CancellationToken cancellationToken)
        => await ReadAndRunAsync(
            file,
            async ofx =>
            {
                var result = await importer.ImportAsync(ofx, cancellationToken);
                return Results.Ok(new NedbankImportResponse(
                    file.FileName,
                    result.LinesImported,
                    result.StatementReference,
                    result.PeriodStart,
                    result.PeriodEnd,
                    result.OpeningBalance,
                    result.ClosingBalance,
                    result.AlreadyImportedCount));
            },
            cancellationToken);

    private static async Task<IResult> ReadAndRunAsync(
        IFormFile file,
        Func<string, Task<IResult>> run,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return Results.BadRequest(new { message = "No file uploaded, or the file is empty." });
        }

        string ofx;
        using (var reader = new StreamReader(file.OpenReadStream()))
        {
            ofx = await reader.ReadToEndAsync(cancellationToken);
        }

        try
        {
            return await run(ofx);
        }
        catch (FormatException exception)
        {
            return Results.BadRequest(new { file = file.FileName, message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            // Configuration or Acumatica-side failure (for example a missing cash account).
            return Results.Problem(exception.Message);
        }
    }

    private static async Task<IResult> RefreshInvestecAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        InvestecBankFeedRefreshService refreshService,
        CancellationToken cancellationToken)
    {
        var to = toDate ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var from = fromDate ?? to.AddDays(-30);
        if (from > to)
        {
            return Results.BadRequest(new { message = "fromDate must be on or before toDate." });
        }

        try
        {
            var result = await refreshService.RefreshAsync(from, to, cancellationToken);
            return Results.Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(exception.Message);
        }
    }
}

/// <summary>How far each cash account is imported.</summary>
public sealed record BankingStatusResponse(IReadOnlyList<BankAccountStatus> Accounts);

/// <summary>
/// One cash account on the bank import page. <paramref name="Available"/> is false when
/// Acumatica could not be read, so the page can say so instead of showing a wrong date.
/// </summary>
public sealed record BankAccountStatus(
    string Bank,
    string CashAccount,
    string Mode,
    bool Available,
    DateOnly? ImportedThrough,
    decimal? ClosingBalance,
    IReadOnlyList<BankStatementSummary> RecentStatements);

/// <summary>Response for a Nedbank OFX import.</summary>
public sealed record NedbankImportResponse(
    string FileName,
    int LinesImported,
    string? StatementReference,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    decimal OpeningBalance,
    decimal ClosingBalance,
    int AlreadyImportedCount);
