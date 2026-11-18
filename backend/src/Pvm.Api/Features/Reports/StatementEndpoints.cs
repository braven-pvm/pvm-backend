using System.Globalization;
using Pvm.Infrastructure.Reporting.Statements;

namespace Pvm.Api.Features.Reports;

/// <summary>
/// Merchandiser statement endpoints. Refer to docs/design/merchandiser-statements.md.
/// <list type="bullet">
///   <item><c>GET /api/reports/statements/companies</c>: the companies with settings.</item>
///   <item><c>GET /api/reports/statements?company=…&amp;from=2026-10&amp;to=2026-10</c>: one statement.</item>
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
