using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Pvm.Application.Messaging;
using Pvm.Domain.Reporting;
using Pvm.Infrastructure.Persistence;

namespace Pvm.Infrastructure.Reporting.SalesReport;

// The sales pivot: the sell-in report view grouped by any dimensions in any order, with a filter
// of many values for each dimension, a range of months and a comparison with the year before.
// Refer to docs/design/sales-report-pages.md, section "Pivot".
//
// The SQL is built from the fixed expressions in Sql below and from numbers only. Every value that
// a person gives (keys, search text, months) goes in as a parameter.

/// <summary>A dimension that the pivot can group or filter by.</summary>
public enum SalesDimension
{
    Rep,
    RepAtSale,
    Channel,
    CustomerGroup,
    Customer,
    Province,
    ItemGroup,
    Product,
    Warehouse,
}

public enum SalesPivotMeasure
{
    Amount,
    Quantity,

    /// <summary>The number of distinct customers.</summary>
    Customers,
}

public enum SalesPivotColumns
{
    Months,
    Years,

    /// <summary>No columns: only the total of each row.</summary>
    None,
}

/// <param name="From">The first month, as financial year * 100 + financial month (202701 is March 2026).</param>
/// <param name="To">The last month, in the same form.</param>
/// <param name="Include">For each dimension, the keys to keep. A dimension that is not in the map keeps all.</param>
public sealed record SalesPivotFilter(
    int From,
    int To,
    IReadOnlyDictionary<SalesDimension, IReadOnlyList<string>> Include,
    SalesPivotMeasure Measure = SalesPivotMeasure.Amount)
{
    public static readonly IReadOnlyDictionary<SalesDimension, IReadOnlyList<string>> All = new Dictionary<SalesDimension, IReadOnlyList<string>>();
}

/// <param name="Rows">The dimensions of the rows, in the order of the drill-down.</param>
/// <param name="Compare">Also give the values of the same months one year before.</param>
/// <param name="Open">The keys of the open rows. A key is the path of the row, such as "R1|C1".</param>
/// <param name="SortByLabel">Sort each level by label. Otherwise sort by total, the largest first.</param>
public sealed record SalesPivotRequest(
    SalesPivotFilter Filter,
    IReadOnlyList<SalesDimension> Rows,
    SalesPivotColumns Columns = SalesPivotColumns.Months,
    bool Compare = false,
    IReadOnlyCollection<string>? Open = null,
    bool SortByLabel = false);

public sealed record SalesPivotColumn(int Key, string Label);

/// <param name="Previous">The values one year before, when the request compares. Otherwise null.</param>
public sealed record SalesPivotRow(
    int Level,
    string Key,
    string Label,
    IReadOnlyList<decimal> Values,
    decimal Total,
    IReadOnlyList<decimal>? Previous,
    decimal? PreviousTotal,
    bool HasChildren);

public sealed record SalesPivotTotals(IReadOnlyList<decimal> Values, decimal Total, IReadOnlyList<decimal>? Previous, decimal? PreviousTotal);

public sealed record SalesPivot(
    IReadOnlyList<SalesDimension> Dimensions,
    IReadOnlyList<SalesPivotColumn> Columns,
    IReadOnlyList<SalesPivotRow> Rows,
    SalesPivotTotals Totals,
    DateTimeOffset? DataTo);

/// <summary>One value of a dimension, with its total in the filter.</summary>
public sealed record SalesDimensionValue(string Key, string Label, decimal Total);

/// <param name="FirstMonth">The first month with sales, as financial year * 100 + financial month.</param>
public sealed record SalesPivotMeta(int? FirstMonth, int? LastMonth, DateTimeOffset? DataTo);

public sealed class SalesPivotService(PvmDbContext db)
{
    public const int MaxMonths = 36;
    public const int MaxDepth = 5;
    public const string PathSeparator = "|";

    private const string Source = """
        reporting.sell_in_report_lines l
        left join reporting.customer_class_channels ch on ch."CustomerClassId" = l."CustomerClassId"
        """;

    private const string MonthKey = """(l."FinancialYear" * 100 + l."FinancialMonth")""";

    private static readonly IReadOnlyDictionary<SalesDimension, (string Key, string Label)> Sql = new Dictionary<SalesDimension, (string, string)>
    {
        [SalesDimension.Rep] = ("""coalesce(l."SalesRepId", '')""", """coalesce(l."SalesRepName", l."SalesRepId", 'No rep')"""),
        [SalesDimension.RepAtSale] = ("""coalesce(l."SalesRepAtSaleId", '')""", """coalesce(l."SalesRepAtSaleName", l."SalesRepAtSaleId", 'No rep')"""),
        [SalesDimension.Channel] = ("""coalesce(ch."Channel", 'Other')""", """coalesce(ch."Channel", 'Other')"""),
        [SalesDimension.CustomerGroup] = ("""coalesce(l."CustomerClassId", '')""", """coalesce(l."CustomerClassName", l."CustomerClassId", 'No group')"""),
        [SalesDimension.Customer] = ("""l."CustomerId" """, """coalesce(l."CustomerName", l."CustomerId")"""),
        [SalesDimension.Province] = ("""coalesce(l."Province", '')""", """coalesce(nullif(l."Province", ''), 'No province')"""),
        [SalesDimension.ItemGroup] = ("""coalesce(l."ItemClassId", '')""", """coalesce(l."ItemClassName", l."ItemClassId", 'No group')"""),
        [SalesDimension.Product] = ("""l."InventoryId" """, """coalesce(l."ItemDescription", l."InventoryId")"""),
        [SalesDimension.Warehouse] = ("""coalesce(l."WarehouseId", '')""", """coalesce(nullif(l."WarehouseId", ''), 'No warehouse')"""),
    };

    /// <summary>The months that have sales, and the end of the last successful load.</summary>
    public async Task<SalesPivotMeta> MetaAsync(CancellationToken cancellationToken)
    {
        var months = db.SellInReportLines.AsNoTracking().Select(line => line.FinancialYear * 100 + line.FinancialMonth);
        var first = await months.Select(month => (int?)month).MinAsync(cancellationToken);
        var last = await months.Select(month => (int?)month).MaxAsync(cancellationToken);
        return new SalesPivotMeta(first, last, await DataToAsync(cancellationToken));
    }

    /// <summary>
    /// The values of one dimension in the filter, the largest first. The selection of the dimension
    /// itself does not apply, so that a person sees the values to add.
    /// </summary>
    public async Task<IReadOnlyList<SalesDimensionValue>> ValuesAsync(
        SalesDimension dimension, SalesPivotFilter filter, string? search, int limit, CancellationToken cancellationToken)
    {
        var include = filter.Include.Where(item => item.Key != dimension).ToDictionary(item => item.Key, item => item.Value);
        var parameters = new List<NpgsqlParameter>();
        var where = Where(filter with { Include = include }, compare: false, parameters);
        var (key, label) = Sql[dimension];
        var searchSql = string.Empty;
        if (!string.IsNullOrWhiteSpace(search))
        {
            parameters.Add(new NpgsqlParameter("search", search.Trim().ToLowerInvariant()));
            searchSql = $"and (position(@search in lower({key})) > 0 or position(@search in lower({label})) > 0)";
        }

        parameters.Add(new NpgsqlParameter("limit", Math.Clamp(limit, 1, 500)));
        var sql = $"""
            select {key} as "Key", min({label}) as "Label", {Measure(filter.Measure, "true")} as "Total"
            from {Source}
            where {where} {searchSql}
            group by 1
            order by 3 desc, 2
            limit @limit
            """;
        return await db.Database.SqlQueryRaw<SalesDimensionValue>(sql, [.. parameters]).ToListAsync(cancellationToken);
    }

    /// <summary>The labels of some keys of a dimension, in any month, for the chips of a selection.</summary>
    public async Task<IReadOnlyList<SalesDimensionValue>> LabelsAsync(SalesDimension dimension, IReadOnlyList<string> keys, CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return [];
        }

        var (key, label) = Sql[dimension];
        var sql = $"""
            select {key} as "Key", min({label}) as "Label", 0::numeric as "Total"
            from {Source}
            where {key} = any(@keys)
            group by 1
            order by 2
            """;
        var parameter = new NpgsqlParameter("keys", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = keys.Take(500).ToArray() };
        return await db.Database.SqlQueryRaw<SalesDimensionValue>(sql, parameter).ToListAsync(cancellationToken);
    }

    /// <summary>The pivot: the first level, and the children of each open row.</summary>
    public async Task<SalesPivot> PivotAsync(SalesPivotRequest request, CancellationToken cancellationToken)
    {
        var columns = Columns(request.Filter, request.Columns);
        var open = request.Open is null ? null : request.Open.ToHashSet(StringComparer.Ordinal);
        var levels = new List<IReadOnlyList<Cell>>();
        for (var depth = 0; depth < request.Rows.Count; depth++)
        {
            IReadOnlyList<string>? prefixes = null;
            if (depth > 0 && open is not null)
            {
                prefixes = [.. open.Where(key => key.Split(PathSeparator).Length == depth)];
                if (prefixes.Count == 0)
                {
                    break;
                }
            }

            levels.Add(await CellsAsync(request, depth, prefixes, cancellationToken));
        }

        var totals = await CellsAsync(request, -1, null, cancellationToken);
        var nodes = levels.Select((level, depth) => Rows(level, columns, request, depth).ToLookup(row => Parent(row.Key, depth))).ToList();
        var rows = new List<SalesPivotRow>();
        void Emit(int depth, string parent)
        {
            if (depth >= nodes.Count)
            {
                return;
            }

            var children = nodes[depth][parent];
            children = request.SortByLabel
                ? children.OrderBy(row => row.Label, StringComparer.OrdinalIgnoreCase)
                : children.OrderByDescending(row => row.Total).ThenBy(row => row.Label, StringComparer.OrdinalIgnoreCase);
            foreach (var row in children)
            {
                rows.Add(row);
                if (open is null || open.Contains(row.Key))
                {
                    Emit(depth + 1, row.Key);
                }
            }
        }

        Emit(0, string.Empty);
        var total = Rows(totals, columns, request, 0).SingleOrDefault() ?? Empty(columns, request.Compare, 0, string.Empty, string.Empty, false);
        return new SalesPivot(
            request.Rows,
            columns,
            rows,
            new SalesPivotTotals(total.Values, total.Total, total.Previous, total.PreviousTotal),
            await DataToAsync(cancellationToken));
    }

    /// <summary>The pivot with all levels open, as CSV.</summary>
    public async Task<string> CsvAsync(SalesPivotRequest request, CancellationToken cancellationToken)
    {
        var pivot = await PivotAsync(request with { Open = null }, cancellationToken);
        var names = request.Rows.Select(Name).ToList();
        var csv = new StringBuilder();
        var header = new List<string> { "Level" };
        header.AddRange(names);
        header.AddRange(pivot.Columns.Select(column => column.Label));
        header.Add("Total");
        if (request.Compare)
        {
            header.Add("Total a year before");
        }

        csv.Append(string.Join(',', header.Select(Csv))).Append("\r\n");
        var path = new string[names.Count];
        void Line(string level, IEnumerable<string> labels, IReadOnlyList<decimal> values, decimal total, decimal? previous)
        {
            var cells = new List<string> { level };
            cells.AddRange(labels);
            cells.AddRange(values.Select(Number));
            cells.Add(Number(total));
            if (request.Compare)
            {
                cells.Add(Number(previous ?? 0));
            }

            csv.Append(string.Join(',', cells.Select(Csv))).Append("\r\n");
        }

        foreach (var row in pivot.Rows)
        {
            path[row.Level] = row.Label;
            Line(
                row.Level.ToString(CultureInfo.InvariantCulture),
                Enumerable.Range(0, names.Count).Select(index => index <= row.Level ? path[index] : string.Empty),
                row.Values,
                row.Total,
                row.PreviousTotal);
        }

        Line("Total", names.Select(_ => string.Empty), pivot.Totals.Values, pivot.Totals.Total, pivot.Totals.PreviousTotal);
        return csv.ToString();
    }

    /// <summary>The lines behind the pivot, a page at a time.</summary>
    public async Task<SalesReportLinePage> LinesAsync(SalesPivotFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var parameters = new List<NpgsqlParameter>();
        var where = Where(filter, compare: false, parameters);
        var countSql = $"""select count(*)::int as "Value" from {Source} where {where}""";
        var count = await db.Database.SqlQueryRaw<int>(countSql, [.. Copy(parameters)]).SingleAsync(cancellationToken);
        parameters.Add(new NpgsqlParameter("skip", (page - 1) * pageSize));
        parameters.Add(new NpgsqlParameter("take", pageSize));
        var sql = $"""
            select l."DocumentType" as "DocumentType", l."ReferenceNumber" as "ReferenceNumber", l."DocumentDate" as "DocumentDate",
                l."FinancialYear" as "FinancialYear", l."FinancialMonth" as "FinancialMonth", l."CustomerId" as "CustomerId",
                l."CustomerName" as "CustomerName", l."CustomerClassName" as "CustomerGroup", l."SalesRepName" as "Rep",
                l."InventoryId" as "InventoryId", l."ItemDescription" as "Product", l."ItemClassName" as "ItemGroup",
                l."Quantity" as "Quantity", l."Amount" as "Amount"
            from {Source}
            where {where}
            order by l."FinancialYear", l."FinancialMonth", l."ReferenceNumber", l."LineNumber"
            offset @skip limit @take
            """;
        var lines = await db.Database.SqlQueryRaw<SalesReportLine>(sql, [.. parameters]).ToListAsync(cancellationToken);
        return new SalesReportLinePage(count, page, pageSize, lines);
    }

    /// <summary>The name of a dimension, as the pages show it.</summary>
    public static string Name(SalesDimension dimension) => dimension switch
    {
        SalesDimension.Rep => "Rep",
        SalesDimension.RepAtSale => "Rep at the sale",
        SalesDimension.Channel => "Channel",
        SalesDimension.CustomerGroup => "Customer group",
        SalesDimension.Customer => "Customer",
        SalesDimension.Province => "Province",
        SalesDimension.ItemGroup => "Item group",
        SalesDimension.Product => "Product",
        SalesDimension.Warehouse => "Warehouse",
        _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, null),
    };

    /// <summary>The label of a month, such as "Mar 2026" for 202701.</summary>
    public static string MonthLabel(int month) =>
        FinancialMonth.Create(month / 100, month % 100).FirstDay.ToString("MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>The months from <paramref name="from"/> to <paramref name="to"/>, as financial year * 100 + financial month.</summary>
    public static IReadOnlyList<int> Months(int from, int to)
    {
        var months = new List<int>();
        for (var month = from; month <= to; month = month % 100 == 12 ? (month / 100 + 1) * 100 + 1 : month + 1)
        {
            months.Add(month);
        }

        return months;
    }

    private async Task<IReadOnlyList<Cell>> CellsAsync(SalesPivotRequest request, int depth, IReadOnlyList<string>? prefixes, CancellationToken cancellationToken)
    {
        var parameters = new List<NpgsqlParameter>();
        var where = Where(request.Filter, request.Compare, parameters);
        var current = $"{MonthKey} between @from and @to";
        var column = request.Columns switch
        {
            SalesPivotColumns.Months => $"case when {current} then {MonthKey} else {MonthKey} + 100 end",
            SalesPivotColumns.Years => $"""case when {current} then l."FinancialYear" else l."FinancialYear" + 1 end""",
            _ => """(l."FinancialYear" * 0)""",
        };

        string path;
        string label;
        if (depth < 0)
        {
            path = "''";
            label = "''";
        }
        else
        {
            var keys = request.Rows.Take(depth + 1).Select(dimension => Sql[dimension].Key).ToList();
            path = $"concat_ws('{PathSeparator}', {string.Join(", ", keys)})";
            label = $"min({Sql[request.Rows[depth]].Label})";
            if (prefixes is not null)
            {
                parameters.Add(new NpgsqlParameter("prefixes", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = prefixes.ToArray() });
                where += $" and concat_ws('{PathSeparator}', {string.Join(", ", keys.Take(depth))}) = any(@prefixes)";
            }
        }

        // The totals row groups by the column only. PostgreSQL refuses a text constant in GROUP BY.
        var sets = depth < 0 ? $"({column}), ()" : $"({path}, {column}), ({path})";
        var sql = $"""
            select {path} as "Path", {label} as "Label", coalesce({column}, 0) as "Column", grouping({column}) as "Whole",
                {Measure(request.Filter.Measure, current)} as "Current",
                {Measure(request.Filter.Measure, $"not ({current})")} as "Previous"
            from {Source}
            where {where}
            group by grouping sets ({sets})
            """;
        return await db.Database.SqlQueryRaw<Cell>(sql, [.. parameters]).ToListAsync(cancellationToken);
    }

    private static string Measure(SalesPivotMeasure measure, string when) => measure switch
    {
        SalesPivotMeasure.Quantity => $"""coalesce(sum(case when {when} then l."Quantity" else 0 end), 0)""",
        SalesPivotMeasure.Customers => $"""count(distinct case when {when} then l."CustomerId" end)::numeric""",
        _ => $"""coalesce(sum(case when {when} then l."Amount" else 0 end), 0)""",
    };

    private static string Where(SalesPivotFilter filter, bool compare, List<NpgsqlParameter> parameters)
    {
        parameters.Add(new NpgsqlParameter("from", filter.From));
        parameters.Add(new NpgsqlParameter("to", filter.To));
        var where = new StringBuilder(compare
            ? $"({MonthKey} between @from and @to or {MonthKey} between @from - 100 and @to - 100)"
            : $"{MonthKey} between @from and @to");
        var index = 0;
        foreach (var (dimension, keys) in filter.Include.OrderBy(item => item.Key))
        {
            if (keys.Count == 0)
            {
                continue;
            }

            var name = $"in{index++}";
            parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = keys.ToArray() });
            where.Append(CultureInfo.InvariantCulture, $" and {Sql[dimension].Key} = any(@{name})");
        }

        return where.ToString();
    }

    private static IEnumerable<NpgsqlParameter> Copy(IEnumerable<NpgsqlParameter> parameters) =>
        parameters.Select(parameter => parameter.Clone());

    private static IReadOnlyList<SalesPivotColumn> Columns(SalesPivotFilter filter, SalesPivotColumns columns) => columns switch
    {
        SalesPivotColumns.Months => [.. Months(filter.From, filter.To).Select(month => new SalesPivotColumn(month, MonthLabel(month)))],
        SalesPivotColumns.Years => [.. Enumerable.Range(filter.From / 100, filter.To / 100 - filter.From / 100 + 1)
            .Select(year => new SalesPivotColumn(year, $"FY{year}"))],
        _ => [],
    };

    private static List<SalesPivotRow> Rows(IReadOnlyList<Cell> cells, IReadOnlyList<SalesPivotColumn> columns, SalesPivotRequest request, int level)
    {
        var index = columns.Select((column, position) => (column.Key, position)).ToDictionary(item => item.Key, item => item.position);
        var depth = request.Rows.Count;
        var rows = new List<SalesPivotRow>();
        foreach (var group in cells.GroupBy(cell => cell.Path))
        {
            var whole = group.FirstOrDefault(cell => cell.Whole == 1);
            var row = Empty(columns, request.Compare, level, group.Key, whole?.Label ?? string.Empty, level < depth - 1);
            var values = (decimal[])row.Values;
            var previous = (decimal[]?)row.Previous;
            foreach (var cell in group.Where(cell => cell.Whole == 0))
            {
                if (index.TryGetValue(cell.Column, out var position))
                {
                    values[position] += cell.Current;
                    if (previous is not null)
                    {
                        previous[position] += cell.Previous;
                    }
                }
            }

            rows.Add(row with
            {
                Total = whole?.Current ?? 0,
                PreviousTotal = request.Compare ? whole?.Previous ?? 0 : null,
            });
        }

        return rows;
    }

    private static SalesPivotRow Empty(IReadOnlyList<SalesPivotColumn> columns, bool compare, int level, string key, string label, bool hasChildren) =>
        new(level, key, label, new decimal[columns.Count], 0, compare ? new decimal[columns.Count] : null, compare ? 0 : null, hasChildren);

    private static string Parent(string key, int depth) =>
        depth == 0 ? string.Empty : string.Join(PathSeparator, key.Split(PathSeparator).Take(depth));

    private async Task<DateTimeOffset?> DataToAsync(CancellationToken cancellationToken) =>
        await db.IntegrationRuns.AsNoTracking()
            .Where(run => run.RunType == SellInSyncService.RunType && run.Status == IntegrationRunStatuses.Succeeded)
            .MaxAsync(run => run.CompletedAt, cancellationToken);

    private static string Number(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Csv(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;

    /// <summary>One cell of a query: a row and a column, or the whole row when <see cref="Whole"/> is 1.</summary>
    private sealed class Cell
    {
        public required string Path { get; init; }
        public required string Label { get; init; }
        public int Column { get; init; }
        public int Whole { get; init; }
        public decimal Current { get; init; }
        public decimal Previous { get; init; }
    }
}
