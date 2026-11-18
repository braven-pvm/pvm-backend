using System.Globalization;
using Pvm.Api.Auth;
using Pvm.Infrastructure.Reporting.Statements;

namespace Pvm.Api.Features.Reports;

/// <summary>
/// Merchandiser statement endpoints. Refer to docs/design/merchandiser-statements.md.
/// <list type="bullet">
///   <item><c>GET /api/reports/statements/companies</c>: the companies with settings.</item>
///   <item><c>GET /api/reports/statements?company=…&amp;from=2026-10&amp;to=2026-10</c>: one statement.</item>
///   <item><c>GET /api/reports/statements/export?…</c>: the same statement as an Excel file.</item>
///   <item><c>GET /api/reports/statements/settings?company=…</c>: the settings of a company, with names.</item>
///   <item><c>PUT /api/reports/statements/settings</c>: save the settings of a company. Admin only.</item>
/// </list>
/// </summary>
public static class StatementEndpoints
{
    /// <summary>A statement covers at most one financial year.</summary>
    internal const int MaxMonths = 12;

    public static IEndpointRouteBuilder MapStatementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports/statements");
        group.MapGet("/companies", ListCompaniesAsync).RequireAuthorization("Invoices.Write");
        group.MapGet(string.Empty, GetStatementAsync).RequireAuthorization("Invoices.Write");
        group.MapGet("/export", ExportStatementAsync).RequireAuthorization("Invoices.Write");
        group.MapGet("/settings", GetSettingsAsync).RequireAuthorization("Invoices.Write");
        group.MapPut("/settings", PutSettingsAsync).RequireAuthorization("Admin");
        return app;
    }

    private static async Task<IResult> ListCompaniesAsync(MerchandiserStatementService service, CancellationToken cancellationToken) =>
        Results.Ok(await service.ListCompaniesAsync(cancellationToken));

    private static async Task<IResult> GetStatementAsync(
        string? company,
        string? from,
        string? to,
        MerchandiserStatementService service,
        CancellationToken cancellationToken)
    {
        var (period, problem) = ReadPeriod(company, from, to);
        if (problem is not null)
        {
            return Results.BadRequest(new { message = problem });
        }

        var statement = await service.BuildAsync(company!.Trim(), period.First, period.Last, cancellationToken);
        return statement is null
            ? Results.NotFound(new { message = $"There are no settings for {company} in {period.Last:MMM yyyy}." })
            : Results.Ok(statement);
    }

    private static async Task<IResult> ExportStatementAsync(
        string? company,
        string? from,
        string? to,
        MerchandiserStatementService service,
        CancellationToken cancellationToken)
    {
        var (period, problem) = ReadPeriod(company, from, to);
        if (problem is not null)
        {
            return Results.BadRequest(new { message = problem });
        }

        var statement = await service.BuildAsync(company!.Trim(), period.First, period.Last, cancellationToken);
        return statement is null
            ? Results.NotFound(new { message = $"There are no settings for {company} in {period.Last:MMM yyyy}." })
            : Results.File(
                StatementWorkbook.Write(statement),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                StatementWorkbook.FileName(statement));
    }

    private static async Task<IResult> GetSettingsAsync(string? company, MerchandiserSettingsService service, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(company))
        {
            return Results.BadRequest(new { message = "Choose a company." });
        }

        var settings = await service.ExportAsync(company.Trim(), cancellationToken);
        return settings is null ? Results.NotFound(new { message = $"There are no settings for {company}." }) : Results.Ok(settings);
    }

    private static async Task<IResult> PutSettingsAsync(
        MerchandiserSettingsDocument document,
        MerchandiserSettingsService service,
        CurrentAppUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var result = await service.ImportAsync(document, currentUser.User?.Email ?? "unknown", cancellationToken);
        return result.Saved ? Results.Ok(result) : Results.BadRequest(result);
    }

    /// <summary>Reads "2026-10" months. An empty "to" is the same month as "from".</summary>
    internal static ((DateOnly First, DateOnly Last) Period, string? Problem) ReadPeriod(string? company, string? from, string? to)
    {
        if (string.IsNullOrWhiteSpace(company))
        {
            return (default, "Choose a company.");
        }

        if (!TryMonth(from, out var first))
        {
            return (default, "Give the month as 2026-10.");
        }

        var last = first;
        if (!string.IsNullOrWhiteSpace(to) && !TryMonth(to, out last))
        {
            return (default, "Give the last month as 2026-10.");
        }

        if (last < first)
        {
            return (default, "The last month is before the first month.");
        }

        if (((last.Year - first.Year) * 12) + last.Month - first.Month + 1 > MaxMonths)
        {
            return (default, "A statement covers at most 12 months.");
        }

        return ((first, last), null);
    }

    private static bool TryMonth(string? text, out DateOnly month) =>
        DateOnly.TryParseExact((text ?? string.Empty).Trim() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out month);
}
