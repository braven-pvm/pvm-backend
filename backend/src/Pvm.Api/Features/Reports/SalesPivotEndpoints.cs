using System.Globalization;
using System.Text;
using Pvm.Api.Auth;
using Pvm.Domain.Reporting;
using Pvm.Infrastructure.Reporting.SalesReport;

namespace Pvm.Api.Features.Reports;

/// <summary>
/// The sales pivot and the saved filters. Refer to docs/design/sales-report-pages.md, section "Pivot".
/// <list type="bullet">
///   <item><c>GET /api/reports/sales/meta</c>: the months with sales, and the end of the last load.</item>
///   <item><c>GET /api/reports/sales/pivot?rows=rep,customer&amp;from=2026-03&amp;to=2027-02&amp;rep=R1,R2&amp;open=R1&amp;format=xlsx</c>: the pivot, or an Excel or CSV file of all levels.</item>
///   <item><c>GET /api/reports/sales/values?dimension=customer&amp;search=spar&amp;…</c>: the values of one dimension in the filter.</item>
///   <item><c>GET /api/reports/sales/labels?dimension=customer&amp;keys=C1,C2</c>: the labels of some keys.</item>
///   <item><c>GET /api/reports/sales/lines?page=1&amp;…</c>: the lines, 100 for each page.</item>
///   <item><c>GET, POST /api/reports/sales/saved-filters</c> and <c>PUT, DELETE /api/reports/sales/saved-filters/{id}</c>: the named filters.</item>
/// </list>
/// </summary>
public static class SalesPivotEndpoints
{
    internal const int PageSize = 100;

    internal static readonly IReadOnlyDictionary<string, SalesDimension> Dimensions = new Dictionary<string, SalesDimension>(StringComparer.OrdinalIgnoreCase)
    {
        ["rep"] = SalesDimension.Rep,
        ["repAtSale"] = SalesDimension.RepAtSale,
        ["channel"] = SalesDimension.Channel,
        ["customerGroup"] = SalesDimension.CustomerGroup,
        ["customer"] = SalesDimension.Customer,
        ["province"] = SalesDimension.Province,
        ["itemGroup"] = SalesDimension.ItemGroup,
        ["product"] = SalesDimension.Product,
        ["warehouse"] = SalesDimension.Warehouse,
    };

    private static readonly SalesDimension[] DefaultRows = [SalesDimension.Rep, SalesDimension.Customer, SalesDimension.Product];

    public static IEndpointRouteBuilder MapSalesPivotEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports/sales").RequireAuthorization("Invoices.Write");
        group.MapGet("/meta", async (SalesPivotService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.MetaAsync(cancellationToken)));
        group.MapGet("/pivot", GetPivotAsync);
        group.MapGet("/values", GetValuesAsync);
        group.MapGet("/labels", GetLabelsAsync);
        group.MapGet("/lines", GetLinesAsync);
        group.MapGet("/saved-filters", ListSavedAsync);
        group.MapPost("/saved-filters", CreateSavedAsync);
        group.MapPut("/saved-filters/{id:guid}", UpdateSavedAsync);
        group.MapDelete("/saved-filters/{id:guid}", DeleteSavedAsync);
        return app;
    }

    private static async Task<IResult> GetPivotAsync(HttpRequest http, SalesPivotService service, TimeProvider time, CancellationToken cancellationToken)
    {
        var (request, problem) = ReadRequest(http.Query, Today(time));
        if (request is null)
        {
            return Results.BadRequest(new { message = problem });
        }

        if (string.Equals(http.Query["format"], "xlsx", StringComparison.OrdinalIgnoreCase))
        {
            var workbook = await service.WorkbookAsync(request, cancellationToken);
            return Results.File(workbook, SalesPivotWorkbook.ContentType, Path.ChangeExtension(CsvFileName(request), ".xlsx"));
        }

        if (string.Equals(http.Query["format"], "csv", StringComparison.OrdinalIgnoreCase))
        {
            var csv = await service.CsvAsync(request, cancellationToken);
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv", CsvFileName(request));
        }

        return Results.Ok(await service.PivotAsync(request, cancellationToken));
    }

    private static async Task<IResult> GetValuesAsync(HttpRequest http, SalesPivotService service, TimeProvider time, CancellationToken cancellationToken)
    {
        if (!Dimensions.TryGetValue(http.Query["dimension"].ToString(), out var dimension))
        {
            return Results.BadRequest(new { message = $"Give a dimension: {string.Join(", ", Dimensions.Keys)}." });
        }

        var (filter, problem) = ReadFilter(http.Query, Today(time));
        if (filter is null)
        {
            return Results.BadRequest(new { message = problem });
        }

        var limit = int.TryParse(http.Query["limit"], NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 100;
        return Results.Ok(await service.ValuesAsync(dimension, filter, http.Query["search"], limit, cancellationToken));
    }

    private static async Task<IResult> GetLabelsAsync(HttpRequest http, SalesPivotService service, CancellationToken cancellationToken)
    {
        if (!Dimensions.TryGetValue(http.Query["dimension"].ToString(), out var dimension))
        {
            return Results.BadRequest(new { message = $"Give a dimension: {string.Join(", ", Dimensions.Keys)}." });
        }

        return Results.Ok(await service.LabelsAsync(dimension, [.. List(http.Query, "keys").Distinct(StringComparer.Ordinal)], cancellationToken));
    }

    private static async Task<IResult> GetLinesAsync(HttpRequest http, SalesPivotService service, TimeProvider time, CancellationToken cancellationToken)
    {
        var (filter, problem) = ReadFilter(http.Query, Today(time));
        if (filter is null)
        {
            return Results.BadRequest(new { message = problem });
        }

        var page = int.TryParse(http.Query["page"], NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 1;
        return Results.Ok(await service.LinesAsync(filter, page, PageSize, cancellationToken));
    }

    private static async Task<IResult> ListSavedAsync(
        CurrentAppUserAccessor current, SalesReportSavedFilterService service, CancellationToken cancellationToken) =>
        current.User is not { } user
            ? Results.Forbid()
            : Results.Ok(await service.ListAsync(user.Id, cancellationToken));

    private static async Task<IResult> CreateSavedAsync(
        SavedFilterBody body, CurrentAppUserAccessor current, SalesReportSavedFilterService service, CancellationToken cancellationToken)
    {
        if (current.User is not { } user)
        {
            return Results.Forbid();
        }

        var (result, filter) = await service.CreateAsync(
            user.Id, user.DisplayName ?? user.Email, body.Name, body.Query, body.Shared ?? false, cancellationToken);
        return result == SavedFilterResult.Done
            ? Results.Created($"/api/reports/sales/saved-filters/{filter!.Id:D}", filter)
            : Problem(result);
    }

    private static async Task<IResult> UpdateSavedAsync(
        Guid id, SavedFilterBody body, CurrentAppUserAccessor current, SalesReportSavedFilterService service, CancellationToken cancellationToken)
    {
        if (current.User is not { } user)
        {
            return Results.Forbid();
        }

        var (result, filter) = await service.UpdateAsync(id, user.Id, body.Name, body.Query, body.Shared, cancellationToken);
        return result == SavedFilterResult.Done ? Results.Ok(filter) : Problem(result);
    }

    private static async Task<IResult> DeleteSavedAsync(
        Guid id, CurrentAppUserAccessor current, SalesReportSavedFilterService service, CancellationToken cancellationToken)
    {
        if (current.User is not { } user)
        {
            return Results.Forbid();
        }

        var result = await service.DeleteAsync(id, user.Id, cancellationToken);
        return result == SavedFilterResult.Done ? Results.NoContent() : Problem(result);
    }

    private static IResult Problem(SavedFilterResult result) => result switch
    {
        SavedFilterResult.NotFound => Results.NotFound(new { message = "There is no saved filter with this ID." }),
        SavedFilterResult.NotOwner => Results.Json(new { message = "Only the owner can change a saved filter." }, statusCode: StatusCodes.Status403Forbidden),
        SavedFilterResult.NameInUse => Results.Conflict(new { message = "You have a saved filter with this name." }),
        _ => Results.BadRequest(new
        {
            message = $"Give a name of 1 to {SalesReportSavedFilterService.MaxName} characters and a query of at most {SalesReportSavedFilterService.MaxQuery} characters.",
        }),
    };

    /// <summary>Reads the pivot request: the filter, then rows, columns, compare, sort and open.</summary>
    internal static (SalesPivotRequest? Request, string? Problem) ReadRequest(IQueryCollection query, DateOnly today)
    {
        var (filter, problem) = ReadFilter(query, today);
        if (filter is null)
        {
            return (null, problem);
        }

        var rows = new List<SalesDimension>();
        foreach (var name in List(query, "rows"))
        {
            if (!Dimensions.TryGetValue(name, out var dimension))
            {
                return (null, $"There is no dimension {name}. Use {string.Join(", ", Dimensions.Keys)}.");
            }

            if (rows.Contains(dimension))
            {
                return (null, $"The dimension {name} is in the rows two times.");
            }

            rows.Add(dimension);
        }

        if (rows.Count == 0)
        {
            rows.AddRange(DefaultRows);
        }

        if (rows.Count > SalesPivotService.MaxDepth)
        {
            return (null, $"The rows have at most {SalesPivotService.MaxDepth} dimensions.");
        }

        SalesPivotColumns columns;
        switch (query["columns"].ToString().ToLowerInvariant())
        {
            case "" or "months":
                columns = SalesPivotColumns.Months;
                break;
            case "years":
                columns = SalesPivotColumns.Years;
                break;
            case "none":
                columns = SalesPivotColumns.None;
                break;
            default:
                return (null, "The columns are months, years or none.");
        }

        var compare = query["compare"].ToString() is "1" or "true";
        var sortByLabel = string.Equals(query["sort"], "label", StringComparison.OrdinalIgnoreCase);
        // open=* opens every level, so that one request gives a whole tree, such as rep, channel and customer.
        var open = query["open"].ToString() == "*" ? null : List(query, "open").ToList();
        return (new SalesPivotRequest(filter, rows, columns, compare, open, sortByLabel), null);
    }

    /// <summary>
    /// Reads the months, the measure and the values of each dimension. With no months, the filter
    /// is the financial year to date.
    /// </summary>
    internal static (SalesPivotFilter? Filter, string? Problem) ReadFilter(IQueryCollection query, DateOnly today)
    {
        var current = FinancialMonth.FromDate(today);
        int from, to;
        if (!ReadMonth(query["from"], out var fromMonth) || !ReadMonth(query["to"], out var toMonth))
        {
            return (null, "Give the months as 2026-03.");
        }

        from = fromMonth ?? current.FinancialYear * 100 + 1;
        to = toMonth ?? current.FinancialYear * 100 + current.Number;
        if (from > to)
        {
            return (null, "The first month must be before the last month.");
        }

        if (SalesPivotService.Months(from, to).Count > SalesPivotService.MaxMonths)
        {
            return (null, $"A report shows at most {SalesPivotService.MaxMonths} months.");
        }

        SalesPivotMeasure measure;
        switch (query["measure"].ToString().ToLowerInvariant())
        {
            case "" or "amount":
                measure = SalesPivotMeasure.Amount;
                break;
            case "quantity":
                measure = SalesPivotMeasure.Quantity;
                break;
            case "customers":
                measure = SalesPivotMeasure.Customers;
                break;
            default:
                return (null, "The measure is amount, quantity or customers.");
        }

        var include = new Dictionary<SalesDimension, IReadOnlyList<string>>();
        foreach (var (name, dimension) in Dimensions)
        {
            var keys = List(query, name).Distinct(StringComparer.Ordinal).ToList();
            if (keys.Count > 0)
            {
                include[dimension] = keys;
            }
        }

        return (new SalesPivotFilter(from, to, include, measure), null);
    }

    /// <summary>A calendar month such as 2026-03, as financial year * 100 + financial month.</summary>
    internal static bool ReadMonth(string? text, out int? month)
    {
        month = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!DateOnly.TryParseExact(text.Trim() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            || date.Year is < 2015 or > 2100)
        {
            return false;
        }

        var financial = FinancialMonth.FromDate(date);
        month = financial.FinancialYear * 100 + financial.Number;
        return true;
    }

    internal static string CsvFileName(SalesPivotRequest request) =>
        $"sales-{string.Join('-', request.Rows.Select(row => Dimensions.First(item => item.Value == row).Key))}-" +
        $"{Calendar(request.Filter.From)}-to-{Calendar(request.Filter.To)}.csv";

    private static string Calendar(int month) =>
        FinancialMonth.Create(month / 100, month % 100).FirstDay.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>The values of a parameter. A value can list more than one, with commas.</summary>
    private static IEnumerable<string> List(IQueryCollection query, string name) =>
        query[name].SelectMany(value => (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    public sealed record SavedFilterBody(string? Name, string? Query, bool? Shared);
}
