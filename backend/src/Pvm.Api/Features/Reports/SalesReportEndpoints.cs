using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Pvm.Domain.Reporting;
using Pvm.Infrastructure.Reporting.SalesReport;

namespace Pvm.Api.Features.Reports;

/// <summary>The query string of the sales report. Refer to docs/design/sales-report-pages.md.</summary>
public sealed record SalesReportQuery
{
    [FromQuery(Name = "years")] public string? Years { get; init; }
    [FromQuery(Name = "channel")] public string? Channel { get; init; }
    [FromQuery(Name = "rep")] public string? Rep { get; init; }
    [FromQuery(Name = "province")] public string? Province { get; init; }
    [FromQuery(Name = "itemGroup")] public string? ItemGroup { get; init; }
    [FromQuery(Name = "product")] public string? Product { get; init; }
    [FromQuery(Name = "warehouse")] public string? Warehouse { get; init; }
    [FromQuery(Name = "measure")] public string? Measure { get; init; }
}

/// <summary>
/// Sales report endpoints. They only read. Refer to docs/design/sales-report-pages.md.
/// <list type="bullet">
///   <item><c>GET /api/reports/sales/channels</c>: the channels for the filter.</item>
///   <item><c>GET /api/reports/sales/overview?year=2027&amp;…</c>: this financial year against the year before.</item>
///   <item><c>GET /api/reports/sales/data?years=…&amp;page=1</c>: the lines, 100 for each page.</item>
///   <item><c>GET /api/reports/sales/{view}?years=2026,2027&amp;open=R1,R1|RETAIL&amp;format=csv</c>: a grid.</item>
/// </list>
/// </summary>
public static class SalesReportEndpoints
{
    internal const int MaxYears = 3;
    internal const int PageSize = 100;

    public static IEndpointRouteBuilder MapSalesReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports/sales").RequireAuthorization("Invoices.Write");
        group.MapGet("/channels", async (SalesReportService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ChannelsAsync(cancellationToken)));
        group.MapGet("/overview", GetOverviewAsync);
        group.MapGet("/data", GetDataAsync);
        group.MapGet("/{view}", GetGridAsync);
        return app;
    }

    private static async Task<IResult> GetOverviewAsync(
        [AsParameters] SalesReportQuery query, int? year, SalesReportService service, TimeProvider time, CancellationToken cancellationToken)
    {
        var (filter, problem) = ReadFilter(query, Today(time));
        if (filter is null)
        {
            return Results.BadRequest(new { message = problem });
        }

        var financialYear = year ?? FinancialMonth.FromDate(Today(time)).FinancialYear;
        return Results.Ok(await service.OverviewAsync(financialYear, filter, cancellationToken));
    }

    private static async Task<IResult> GetDataAsync(
        [AsParameters] SalesReportQuery query, int? page, SalesReportService service, TimeProvider time, CancellationToken cancellationToken)
    {
        var (filter, problem) = ReadFilter(query, Today(time));
        return filter is null
            ? Results.BadRequest(new { message = problem })
            : Results.Ok(await service.LinesAsync(filter, page ?? 1, PageSize, cancellationToken));
    }

    private static async Task<IResult> GetGridAsync(
        string view,
        [AsParameters] SalesReportQuery query,
        string? then,
        string? open,
        string? format,
        SalesReportService service,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (ReadView(view, then) is not { } reportView)
        {
            return Results.NotFound(new { message = $"There is no sales report view {view}." });
        }

        var (filter, problem) = ReadFilter(query, Today(time));
        if (filter is null)
        {
            return Results.BadRequest(new { message = problem });
        }

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var csv = await service.CsvAsync(reportView, filter, cancellationToken);
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv", CsvFileName(view, filter.FinancialYears));
        }

        return Results.Ok(await service.GridAsync(reportView, filter, ReadOpen(open), cancellationToken));
    }

    internal static SalesReportView? ReadView(string view, string? then) => view switch
    {
        "by-rep" => SalesReportView.ByRep,
        "by-product" when string.Equals(then, "rep", StringComparison.OrdinalIgnoreCase) => SalesReportView.ByProductThenRep,
        "by-product" => SalesReportView.ByProduct,
        "customers" => SalesReportView.Customers,
        _ => null,
    };

    /// <summary>Reads the filters. With no years, the report shows this financial year and the year before.</summary>
    internal static (SalesReportFilter? Filter, string? Problem) ReadFilter(SalesReportQuery query, DateOnly today)
    {
        List<int> years;
        if (string.IsNullOrWhiteSpace(query.Years))
        {
            var current = FinancialMonth.FromDate(today).FinancialYear;
            years = [current - 1, current];
        }
        else
        {
            years = [];
            foreach (var part in query.Years.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var year) || year is < 2015 or > 2100)
                {
                    return (null, "Give the financial years as 2026,2027.");
                }

                years.Add(year);
            }

            years = [.. years.Distinct().Order()];
            if (years.Count == 0)
            {
                return (null, "Give the financial years as 2026,2027.");
            }

            if (years.Count > MaxYears)
            {
                return (null, $"A report shows at most {MaxYears} financial years.");
            }
        }

        SalesReportMeasure measure;
        switch (query.Measure?.Trim().ToLowerInvariant())
        {
            case null or "" or "amount":
                measure = SalesReportMeasure.Amount;
                break;
            case "quantity":
                measure = SalesReportMeasure.Quantity;
                break;
            default:
                return (null, "The measure is amount or quantity.");
        }

        return (new SalesReportFilter(
            years, Clean(query.Channel), Clean(query.Rep), Clean(query.Province), Clean(query.ItemGroup), Clean(query.Product), Clean(query.Warehouse), measure), null);
    }

    internal static IReadOnlyList<string> ReadOpen(string? open) =>
        (open ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    internal static string CsvFileName(string view, IReadOnlyList<int> years) =>
        $"sales-{view}-{string.Join('-', years.Select(year => $"FY{year}"))}.csv";

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
}
