using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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

public sealed class SalesPivotService(PvmDbContext db, IMemoryCache cache)
{
    /// <summary>
    /// The data changes only with a load, so an answer stays in the cache until the next load
    /// or for this time. The key has the end of the last load.
    /// </summary>
    public static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(30);

    /// <summary>A report query over 2 years of lines can take longer than the default of 30 seconds.</summary>
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(120);

    private const string DataToKey = "sales-data-to";

    /// <summary>How long the end of the last load stays in the cache.</summary>
    public static readonly TimeSpan DataToCacheTime = TimeSpan.FromMinutes(1);

    public const int MaxMonths = 36;
    public const int MaxDepth = 5;
    public const string PathSeparator = "|";

    private static readonly IReadOnlyDictionary<SalesDimension, (string Key, string Label)> LineDimensions = new Dictionary<SalesDimension, (string, string)>
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

    /// <summary>Where a query reads: the report lines, or a monthly summary. Each gives the same expressions.</summary>
    private sealed record SqlSource(
        string From,
        string Month,
        string Year,
        IReadOnlyDictionary<SalesDimension, (string Key, string Label)> Dimensions,
        string Amount,
        string Quantity,
        string Customer);

    private const string ChannelJoin = """left join reporting.customer_class_channels ch on ch."CustomerClassId" = """;

    /// <summary>The report lines, for the lines page.</summary>
    private static readonly SqlSource LineSource = new(
        $"""reporting.sell_in_report_lines l {ChannelJoin}l."CustomerClassId" """,
        """(l."FinancialYear" * 100 + l."FinancialMonth")""",
        """l."FinancialYear" """,
        LineDimensions,
        """l."Amount" """,
        """l."Quantity" """,
        """l."CustomerId" """);

    private static readonly IReadOnlyDictionary<SalesDimension, (string Key, string Label)> SummaryDimensions = new Dictionary<SalesDimension, (string, string)>
    {
        [SalesDimension.Rep] = ("s.rep_key", "s.rep_label"),
        [SalesDimension.RepAtSale] = ("s.rep_at_sale_key", "s.rep_at_sale_label"),
        [SalesDimension.Channel] = ("""coalesce(ch."Channel", 'Other')""", """coalesce(ch."Channel", 'Other')"""),
        [SalesDimension.CustomerGroup] = ("s.customer_group_key", "s.customer_group_label"),
        [SalesDimension.Customer] = ("s.customer_key", "s.customer_label"),
        [SalesDimension.Province] = ("s.province_key", "s.province_label"),
        [SalesDimension.ItemGroup] = ("s.item_group_key", "s.item_group_label"),
        [SalesDimension.Product] = ("s.product_key", "s.product_label"),
        [SalesDimension.Warehouse] = ("s.warehouse_key", "s.warehouse_label"),
    };

    /// <summary>
    /// The monthly summary with every dimension. The sell-in load refreshes it. The channel comes
    /// from its small table at query time, so a change of a channel shows at once.
    /// </summary>
    private static readonly SqlSource MonthSource = new(
        $"reporting.{SellInReportViews.MonthsView} s {ChannelJoin}s.customer_group_key",
        "s.month_key",
        "(s.month_key / 100)",
        SummaryDimensions,
        "s.amount",
        "s.quantity",
        "s.customer_key");

    /// <summary>The smaller monthly summary by customer, for a request that uses only these dimensions.</summary>
    private static readonly HashSet<SalesDimension> CustomerDimensions =
        [SalesDimension.Rep, SalesDimension.Channel, SalesDimension.CustomerGroup, SalesDimension.Customer, SalesDimension.Province];

    private static readonly SqlSource CustomerMonthSource = MonthSource with
    {
        From = $"reporting.{SellInReportViews.CustomerMonthsView} s {ChannelJoin}s.customer_group_key",
    };

    /// <summary>The smallest summary that has every dimension of the rows and of the filter.</summary>
    private static SqlSource SourceFor(IEnumerable<SalesDimension> rows, SalesPivotFilter filter) =>
        rows.Concat(filter.Include.Where(item => item.Value.Count > 0).Select(item => item.Key)).All(CustomerDimensions.Contains)
            ? CustomerMonthSource
            : MonthSource;

    /// <summary>The months that have sales, and the end of the last successful load.</summary>
    public async Task<SalesPivotMeta> MetaAsync(CancellationToken cancellationToken)
    {
        var dataTo = await DataToAsync(cancellationToken);
        var key = $"sales-meta|{dataTo?.UtcTicks}";
        if (cache.TryGetValue(key, out SalesPivotMeta? cached) && cached is not null)
        {
            return cached;
        }

        // The index of the summary gives the first and the last month at once.
        var sql = $"""select min(month_key) as "First", max(month_key) as "Last" from reporting.{SellInReportViews.MonthsView}""";
        var range = await db.Database.SqlQueryRaw<MonthRange>(sql).SingleAsync(cancellationToken);
        var meta = new SalesPivotMeta(range.First, range.Last, dataTo);
        cache.Set(key, meta, CacheTime);
        return meta;
    }

    /// <summary>
    /// The values of one dimension in the filter, the largest first. The selection of the dimension
    /// itself does not apply, so that a person sees the values to add.
    /// </summary>
    public async Task<IReadOnlyList<SalesDimensionValue>> ValuesAsync(
        SalesDimension dimension, SalesPivotFilter filter, string? search, int limit, CancellationToken cancellationToken)
    {
        var include = filter.Include.Where(item => item.Key != dimension).ToDictionary(item => item.Key, item => item.Value);
        var dataTo = await DataToAsync(cancellationToken);
        var cacheKey = $"sales-values|{dataTo?.UtcTicks}|{dimension}|{search?.Trim().ToLowerInvariant()}|{limit}|" +
            RequestKey(new SalesPivotRequest(filter with { Include = include }, [dimension]));
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<SalesDimensionValue>? cached) && cached is not null)
        {
            return cached;
        }

        db.Database.SetCommandTimeout(CommandTimeout);
        var source = SourceFor([dimension], filter with { Include = include });
        var parameters = new List<NpgsqlParameter>();
        var where = Where(filter with { Include = include }, compare: false, parameters, source);
        var (key, label) = source.Dimensions[dimension];
        var searchSql = string.Empty;
        if (!string.IsNullOrWhiteSpace(search))
        {
            parameters.Add(new NpgsqlParameter("search", search.Trim().ToLowerInvariant()));
            searchSql = $"and (position(@search in lower({key})) > 0 or position(@search in lower({label})) > 0)";
        }

        parameters.Add(new NpgsqlParameter("limit", Math.Clamp(limit, 1, 500)));
        var sql = $"""
            select {key} as "Key", min({label}) as "Label", {Measure(filter.Measure, "true", source)} as "Total"
            from {source.From}
            where {where} {searchSql}
            group by 1
            order by 3 desc, 2
            limit @limit
            """;
        var values = await db.Database.SqlQueryRaw<SalesDimensionValue>(sql, [.. parameters]).ToListAsync(cancellationToken);
        cache.Set(cacheKey, (IReadOnlyList<SalesDimensionValue>)values, CacheTime);
        return values;
    }

    /// <summary>The labels of some keys of a dimension, in any month, for the chips of a selection.</summary>
    public async Task<IReadOnlyList<SalesDimensionValue>> LabelsAsync(SalesDimension dimension, IReadOnlyList<string> keys, CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return [];
        }

        var (key, label) = MonthSource.Dimensions[dimension];
        var sql = $"""
            select {key} as "Key", min({label}) as "Label", 0::numeric as "Total"
            from {MonthSource.From}
            where {key} = any(@keys)
            group by 1
            order by 2
            """;
        var parameter = new NpgsqlParameter("keys", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = keys.Take(500).ToArray() };
        return await db.Database.SqlQueryRaw<SalesDimensionValue>(sql, parameter).ToListAsync(cancellationToken);
    }

    /// <summary>The pivot: the first level, and the children of each open row. A null Open opens all levels.</summary>
    public async Task<SalesPivot> PivotAsync(SalesPivotRequest request, CancellationToken cancellationToken)
    {
        var dataTo = await DataToAsync(cancellationToken);
        var key = $"sales-pivot|{dataTo?.UtcTicks}|{RequestKey(request)}";
        if (cache.TryGetValue(key, out SalesPivot? cached) && cached is not null)
        {
            return cached;
        }

        db.Database.SetCommandTimeout(CommandTimeout);
        var pivot = await BuildPivotAsync(request, dataTo, cancellationToken);
        cache.Set(key, pivot, CacheTime);
        return pivot;
    }

    /// <summary>A stable text of a request, for the cache key.</summary>
    internal static string RequestKey(SalesPivotRequest request)
    {
        var include = string.Join(";", request.Filter.Include
            .Where(item => item.Value.Count > 0)
            .OrderBy(item => item.Key)
            .Select(item => $"{item.Key}={string.Join(",", item.Value.Order(StringComparer.Ordinal))}"));
        var open = request.Open is null ? "*" : string.Join(",", request.Open.Order(StringComparer.Ordinal));
        return $"{request.Filter.From}-{request.Filter.To}|{request.Filter.Measure}|{include}|{string.Join(",", request.Rows)}|{request.Columns}|{request.Compare}|{request.SortByLabel}|{open}";
    }

    private async Task<SalesPivot> BuildPivotAsync(SalesPivotRequest request, DateTimeOffset? dataTo, CancellationToken cancellationToken)
    {
        var columns = Columns(request.Filter, request.Columns);
        var source = SourceFor(request.Rows, request.Filter);
        var open = request.Open is null ? null : request.Open.ToHashSet(StringComparer.Ordinal);
        var levels = new List<IReadOnlyList<Cell>>();
        IReadOnlyList<Cell> totals;
        if (open is null)
        {
            // The whole tree: every level and the totals in one read.
            var cells = await TreeCellsAsync(request, source, cancellationToken);
            for (var depth = 0; depth < request.Rows.Count; depth++)
            {
                levels.Add([.. cells.Where(cell => cell.Level == depth)]);
            }

            totals = [.. cells.Where(cell => cell.Level < 0)];
        }
        else
        {
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

            levels.Add(await CellsAsync(request, source, depth, prefixes, cancellationToken));
        }

        totals = await CellsAsync(request, source, -1, null, cancellationToken);
        }

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
            dataTo);
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

    /// <summary>
    /// The pivot with all levels, as an Excel workbook. The top of the sheet names the period, the
    /// comparison, the measure, each filter with its labels, and the load that the figures come from.
    /// </summary>
    public async Task<byte[]> WorkbookAsync(SalesPivotRequest request, CancellationToken cancellationToken)
    {
        var pivot = await PivotAsync(request with { Open = null }, cancellationToken);
        var notes = new List<SalesWorkbookNote>
        {
            new("Period", $"{MonthLabel(request.Filter.From)} to {MonthLabel(request.Filter.To)}"),
        };
        if (request.Compare)
        {
            notes.Add(new("Compared with", $"{MonthLabel(request.Filter.From - 100)} to {MonthLabel(request.Filter.To - 100)}"));
        }

        notes.Add(new("Measure", MeasureName(request.Filter.Measure)));
        foreach (var (dimension, keys) in request.Filter.Include.Where(item => item.Value.Count > 0).OrderBy(item => item.Key))
        {
            var labels = (await LabelsAsync(dimension, keys, cancellationToken)).ToDictionary(value => value.Key, value => value.Label);
            notes.Add(new(Name(dimension), string.Join(", ", keys.Select(key => labels.GetValueOrDefault(key, key)))));
        }

        notes.Add(new("Data", pivot.DataTo is { } dataTo
            ? $"Sell-in from Acumatica, from the load of {dataTo.ToOffset(TimeSpan.FromHours(2)).ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture)} (South African time). Credit notes are subtracted."
            : "Sell-in from Acumatica. No load has finished yet."));
        var rowHeader = string.Join(", then ", request.Rows.Select(Name));
        return SalesPivotWorkbook.Write(pivot, $"Sales report by {rowHeader.ToLowerInvariant()}", notes, rowHeader, MeasureName(request.Filter.Measure));
    }

    public static string MeasureName(SalesPivotMeasure measure) => measure switch
    {
        SalesPivotMeasure.Quantity => "Quantity",
        SalesPivotMeasure.Customers => "Customers",
        _ => "Sales (R)",
    };

    /// <summary>The lines behind the pivot, a page at a time.</summary>
    public async Task<SalesReportLinePage> LinesAsync(SalesPivotFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var parameters = new List<NpgsqlParameter>();
        var where = Where(filter, compare: false, parameters, LineSource);
        var countSql = $"""select count(*)::int as "Value" from {LineSource.From} where {where}""";
        var count = await db.Database.SqlQueryRaw<int>(countSql, [.. Copy(parameters)]).SingleAsync(cancellationToken);
        parameters.Add(new NpgsqlParameter("skip", (page - 1) * pageSize));
        parameters.Add(new NpgsqlParameter("take", pageSize));
        var sql = $"""
            select l."DocumentType" as "DocumentType", l."ReferenceNumber" as "ReferenceNumber", l."DocumentDate" as "DocumentDate",
                l."FinancialYear" as "FinancialYear", l."FinancialMonth" as "FinancialMonth", l."CustomerId" as "CustomerId",
                l."CustomerName" as "CustomerName", l."CustomerClassName" as "CustomerGroup", l."SalesRepName" as "Rep",
                l."InventoryId" as "InventoryId", l."ItemDescription" as "Product", l."ItemClassName" as "ItemGroup",
                l."Quantity" as "Quantity", l."Amount" as "Amount"
            from {LineSource.From}
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

    private static string ColumnOf(SalesPivotRequest request, SqlSource source, string current) => request.Columns switch
    {
        SalesPivotColumns.Months => $"case when {current} then {source.Month} else {source.Month} + 100 end",
        SalesPivotColumns.Years => $"case when {current} then {source.Year} else {source.Year} + 1 end",
        _ => $"({source.Year} * 0)",
    };

    /// <summary>
    /// Every level of the rows and the totals in one read, with grouping sets. A key that a set
    /// leaves out is null, so the path of a row has only its own levels.
    /// </summary>
    private async Task<IReadOnlyList<Cell>> TreeCellsAsync(SalesPivotRequest request, SqlSource source, CancellationToken cancellationToken)
    {
        var parameters = new List<NpgsqlParameter>();
        var where = Where(request.Filter, request.Compare, parameters, source);
        var current = $"{source.Month} between @from and @to";
        var column = ColumnOf(request, source, current);
        var keys = request.Rows.Select(dimension => source.Dimensions[dimension].Key).ToList();
        var labels = request.Rows.Select(dimension => source.Dimensions[dimension].Label).ToList();
        var sets = new List<string>();
        for (var depth = 0; depth < keys.Count; depth++)
        {
            var path = string.Join(", ", keys.Take(depth + 1));
            sets.Add($"({path}, {column})");
            sets.Add($"({path})");
        }

        sets.Add($"({column})");
        sets.Add("()");
        var level = new StringBuilder("case");
        var label = new StringBuilder("case");
        for (var depth = keys.Count - 1; depth >= 0; depth--)
        {
            level.Append(CultureInfo.InvariantCulture, $" when grouping({keys[depth]}) = 0 then {depth}");
            label.Append(CultureInfo.InvariantCulture, $" when grouping({keys[depth]}) = 0 then min({labels[depth]})");
        }

        level.Append(" else -1 end");
        label.Append(" else '' end");
        var sql = $"""
            select coalesce(concat_ws('{PathSeparator}', {string.Join(", ", keys)}), '') as "Path", {label} as "Label", {level} as "Level",
                coalesce({column}, 0) as "Column", grouping({column}) as "Whole",
                {Measure(request.Filter.Measure, current, source)} as "Current",
                {Measure(request.Filter.Measure, $"not ({current})", source)} as "Previous"
            from {source.From}
            where {where}
            group by grouping sets ({string.Join(", ", sets)})
            """;
        return await db.Database.SqlQueryRaw<Cell>(sql, [.. parameters]).ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<Cell>> CellsAsync(SalesPivotRequest request, SqlSource source, int depth, IReadOnlyList<string>? prefixes, CancellationToken cancellationToken)
    {
        var parameters = new List<NpgsqlParameter>();
        var where = Where(request.Filter, request.Compare, parameters, source);
        var current = $"{source.Month} between @from and @to";
        var column = ColumnOf(request, source, current);

        string path;
        string label;
        if (depth < 0)
        {
            path = "''";
            label = "''";
        }
        else
        {
            var keys = request.Rows.Take(depth + 1).Select(dimension => source.Dimensions[dimension].Key).ToList();
            path = $"concat_ws('{PathSeparator}', {string.Join(", ", keys)})";
            label = $"min({source.Dimensions[request.Rows[depth]].Label})";
            if (prefixes is not null)
            {
                parameters.Add(new NpgsqlParameter("prefixes", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = prefixes.ToArray() });
                where += $" and concat_ws('{PathSeparator}', {string.Join(", ", keys.Take(depth))}) = any(@prefixes)";
            }
        }

        // The totals row groups by the column only. PostgreSQL refuses a text constant in GROUP BY.
        var sets = depth < 0 ? $"({column}), ()" : $"({path}, {column}), ({path})";
        var sql = $"""
            select {path} as "Path", {label} as "Label", {depth} as "Level", coalesce({column}, 0) as "Column", grouping({column}) as "Whole",
                {Measure(request.Filter.Measure, current, source)} as "Current",
                {Measure(request.Filter.Measure, $"not ({current})", source)} as "Previous"
            from {source.From}
            where {where}
            group by grouping sets ({sets})
            """;
        return await db.Database.SqlQueryRaw<Cell>(sql, [.. parameters]).ToListAsync(cancellationToken);
    }

    private static string Measure(SalesPivotMeasure measure, string when, SqlSource source) => measure switch
    {
        SalesPivotMeasure.Quantity => $"coalesce(sum(case when {when} then {source.Quantity} else 0 end), 0)",
        SalesPivotMeasure.Customers => $"count(distinct case when {when} then {source.Customer} end)::numeric",
        _ => $"coalesce(sum(case when {when} then {source.Amount} else 0 end), 0)",
    };

    private static string Where(SalesPivotFilter filter, bool compare, List<NpgsqlParameter> parameters, SqlSource source)
    {
        parameters.Add(new NpgsqlParameter("from", filter.From));
        parameters.Add(new NpgsqlParameter("to", filter.To));
        var month = source.Month;
        var where = new StringBuilder(compare
            ? $"({month} between @from and @to or {month} between @from - 100 and @to - 100)"
            : $"{month} between @from and @to");
        var index = 0;
        foreach (var (dimension, keys) in filter.Include.OrderBy(item => item.Key))
        {
            if (keys.Count == 0)
            {
                continue;
            }

            var name = $"in{index++}";
            parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = keys.ToArray() });
            where.Append(CultureInfo.InvariantCulture, $" and {source.Dimensions[dimension].Key} = any(@{name})");
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

    /// <summary>
    /// The end of the last successful load. Each pivot call needs it for its cache key, so it stays
    /// in the cache for a minute: after a load, the new figures show within that minute.
    /// </summary>
    private async Task<DateTimeOffset?> DataToAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(DataToKey, out DateTimeOffset? cached))
        {
            return cached;
        }

        var dataTo = await db.IntegrationRuns.AsNoTracking()
            .Where(run => run.RunType == SellInSyncService.RunType && run.Status == IntegrationRunStatuses.Succeeded)
            .MaxAsync(run => run.CompletedAt, cancellationToken);
        cache.Set(DataToKey, dataTo, DataToCacheTime);
        return dataTo;
    }

    private static string Number(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Csv(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;

    private sealed class MonthRange
    {
        public int? First { get; init; }
        public int? Last { get; init; }
    }

    /// <summary>One cell of a query: a row and a column, or the whole row when <see cref="Whole"/> is 1.</summary>
    private sealed class Cell
    {
        public required string Path { get; init; }
        public required string Label { get; init; }

        /// <summary>The level of the row, from 0. -1 for the totals.</summary>
        public int Level { get; init; }

        public int Column { get; init; }
        public int Whole { get; init; }
        public decimal Current { get; init; }
        public decimal Previous { get; init; }
    }
}
