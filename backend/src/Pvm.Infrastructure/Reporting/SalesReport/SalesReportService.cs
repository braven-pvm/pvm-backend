using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Pvm.Application.Messaging;
using Pvm.Domain.Reporting;
using Pvm.Infrastructure.Persistence;

namespace Pvm.Infrastructure.Reporting.SalesReport;

// The sales report: grids of the sell-in report view, grouped in SQL, in the levels of the old
// workbook pivots. Refer to docs/design/sales-report-pages.md.

public enum SalesReportView
{
    /// <summary>Rep, then customer group, then customer (the "Sales Rep" pivots).</summary>
    ByRep,

    /// <summary>Item group, then product, then customer group (the "Products" pivots).</summary>
    ByProduct,

    /// <summary>Item group, then product, then rep.</summary>
    ByProductThenRep,

    /// <summary>Distinct customers by rep (the "Client Count" pivot).</summary>
    Customers,
}

public enum SalesReportMeasure
{
    Amount,
    Quantity,
}

/// <param name="FinancialYears">The financial years to show, such as 2026 and 2027.</param>
/// <param name="Channel">A channel of the channel table, or "Other". Empty: all.</param>
public sealed record SalesReportFilter(
    IReadOnlyList<int> FinancialYears,
    string? Channel = null,
    string? RepId = null,
    string? Province = null,
    string? ItemClassId = null,
    string? InventoryId = null,
    string? WarehouseId = null,
    SalesReportMeasure Measure = SalesReportMeasure.Amount);

public sealed record SalesReportColumn(int FinancialYear, int Month, string Label);

/// <param name="Key">The path of the row, such as "R1|RETAIL|C1". Open a row by its key.</param>
public sealed record SalesReportRow(int Level, string Key, string Label, IReadOnlyList<decimal> Values, decimal Total, bool HasChildren);

public sealed record SalesReportTotals(IReadOnlyList<decimal> Values, decimal Total);

/// <param name="DataTo">The end of the last successful sell-in load.</param>
public sealed record SalesReportGrid(
    IReadOnlyList<string> LevelNames,
    IReadOnlyList<SalesReportColumn> Columns,
    IReadOnlyList<SalesReportRow> Rows,
    SalesReportTotals Totals,
    DateTimeOffset? DataTo);

public sealed record SalesReportOverviewMonth(int Month, string Label, decimal ThisYear, decimal LastYear);

public sealed record SalesReportOverview(int FinancialYear, IReadOnlyList<SalesReportOverviewMonth> Months, decimal ThisYearTotal, decimal LastYearTotal, int ActiveCustomers, DateTimeOffset? DataTo);

public sealed record SalesReportLine(
    string DocumentType, string ReferenceNumber, DateOnly DocumentDate, int FinancialYear, int FinancialMonth,
    string CustomerId, string? CustomerName, string? CustomerGroup, string? Rep, string InventoryId, string? Product,
    string? ItemGroup, decimal Quantity, decimal Amount);

public sealed record SalesReportLinePage(int TotalLines, int Page, int PageSize, IReadOnlyList<SalesReportLine> Lines);

public sealed class SalesReportService(PvmDbContext db)
{
    public const string OtherChannel = "Other";

    private const string NoRep = "No rep";
    private const string NoGroup = "No group";

    /// <summary>The channels of the channel table, then "Other".</summary>
    public async Task<IReadOnlyList<string>> ChannelsAsync(CancellationToken cancellationToken)
    {
        var channels = await db.ReportingCustomerClassChannels.AsNoTracking().Select(item => item.Channel).Distinct().ToListAsync(cancellationToken);
        return [.. channels.Where(channel => channel != OtherChannel).Order(StringComparer.Ordinal), OtherChannel];
    }

    /// <summary>A grid with its top level, and the children of each row whose key is in <paramref name="open"/>.</summary>
    public async Task<SalesReportGrid> GridAsync(SalesReportView view, SalesReportFilter filter, IReadOnlyCollection<string> open, CancellationToken cancellationToken)
    {
        var columns = Columns(filter.FinancialYears);
        var dataTo = await DataToAsync(cancellationToken);
        if (view == SalesReportView.Customers)
        {
            return await CustomersAsync(filter, columns, dataTo, cancellationToken);
        }

        var root = await TreeAsync(view, filter, columns, cancellationToken);
        var rows = new List<SalesReportRow>();
        Emit(root, open.ToHashSet(StringComparer.Ordinal), rows);
        return new SalesReportGrid(LevelNames(view), columns, rows, new SalesReportTotals(root.Values, root.Values.Sum()), dataTo);
    }

    /// <summary>The same grid with all levels open, as CSV.</summary>
    public async Task<string> CsvAsync(SalesReportView view, SalesReportFilter filter, CancellationToken cancellationToken)
    {
        var columns = Columns(filter.FinancialYears);
        var names = LevelNames(view);
        var csv = new StringBuilder();
        csv.Append(string.Join(',', new[] { "Level" }.Concat(names).Concat(columns.Select(column => column.Label)).Append("Total").Select(Csv))).Append("\r\n");

        void Row(string level, IReadOnlyList<string> path, IEnumerable<decimal> values, decimal total)
        {
            var cells = new List<string> { level };
            cells.AddRange(Enumerable.Range(0, names.Count).Select(index => index < path.Count ? path[index] : string.Empty));
            cells.AddRange(values.Select(Number));
            cells.Add(Number(total));
            csv.Append(string.Join(',', cells.Select(Csv))).Append("\r\n");
        }

        if (view == SalesReportView.Customers)
        {
            var grid = await CustomersAsync(filter, columns, null, cancellationToken);
            foreach (var row in grid.Rows)
            {
                Row("0", [row.Label], row.Values, row.Total);
            }

            Row("Total", [], grid.Totals.Values, grid.Totals.Total);
            return csv.ToString();
        }

        var root = await TreeAsync(view, filter, columns, cancellationToken);
        void Walk(Node node, List<string> path)
        {
            foreach (var child in node.Sorted())
            {
                var childPath = new List<string>(path) { child.Label };
                Row(child.Level.ToString(CultureInfo.InvariantCulture), childPath, child.Values, child.Values.Sum());
                Walk(child, childPath);
            }
        }

        Walk(root, []);
        Row("Total", [], root.Values, root.Values.Sum());
        return csv.ToString();
    }

    /// <summary>This financial year against the year before, month by month.</summary>
    public async Task<SalesReportOverview> OverviewAsync(int financialYear, SalesReportFilter filter, CancellationToken cancellationToken)
    {
        var lines = Filtered(filter with { FinancialYears = [financialYear - 1, financialYear] });
        var sums = await lines
            .GroupBy(line => new { line.FinancialYear, line.FinancialMonth })
            .Select(group => new { group.Key.FinancialYear, group.Key.FinancialMonth, Amount = group.Sum(line => line.Amount) })
            .ToListAsync(cancellationToken);
        decimal Sum(int year, int month) => sums.Where(item => item.FinancialYear == year && item.FinancialMonth == month).Sum(item => item.Amount);
        var months = Enumerable.Range(1, 12)
            .Select(month => new SalesReportOverviewMonth(month, Label(financialYear, month), Sum(financialYear, month), Sum(financialYear - 1, month)))
            .ToList();
        var active = await lines.Where(line => line.FinancialYear == financialYear).Select(line => line.CustomerId).Distinct().CountAsync(cancellationToken);
        return new SalesReportOverview(
            financialYear, months, months.Sum(month => month.ThisYear), months.Sum(month => month.LastYear), active, await DataToAsync(cancellationToken));
    }

    /// <summary>The lines behind the report, a page at a time.</summary>
    public async Task<SalesReportLinePage> LinesAsync(SalesReportFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var lines = Filtered(filter);
        var total = await lines.CountAsync(cancellationToken);
        var rows = await lines
            .OrderBy(line => line.FinancialYear).ThenBy(line => line.FinancialMonth).ThenBy(line => line.ReferenceNumber).ThenBy(line => line.LineNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(line => new SalesReportLine(
                line.DocumentType.ToString(), line.ReferenceNumber, line.DocumentDate, line.FinancialYear, line.FinancialMonth,
                line.CustomerId, line.CustomerName, line.CustomerClassName, line.SalesRepName, line.InventoryId, line.ItemDescription,
                line.ItemClassName, line.Quantity, line.Amount))
            .ToListAsync(cancellationToken);
        return new SalesReportLinePage(total, page, pageSize, rows);
    }

    private IQueryable<Persistence.Entities.SellInReportLine> Filtered(SalesReportFilter filter)
    {
        var years = filter.FinancialYears.ToList();
        var lines = db.SellInReportLines.AsNoTracking().Where(line => years.Contains(line.FinancialYear));
        if (!string.IsNullOrWhiteSpace(filter.RepId))
        {
            lines = lines.Where(line => line.SalesRepId == filter.RepId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Province))
        {
            lines = lines.Where(line => line.Province == filter.Province);
        }

        if (!string.IsNullOrWhiteSpace(filter.ItemClassId))
        {
            lines = lines.Where(line => line.ItemClassId == filter.ItemClassId);
        }

        if (!string.IsNullOrWhiteSpace(filter.InventoryId))
        {
            lines = lines.Where(line => line.InventoryId == filter.InventoryId);
        }

        if (!string.IsNullOrWhiteSpace(filter.WarehouseId))
        {
            lines = lines.Where(line => line.WarehouseId == filter.WarehouseId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Channel))
        {
            var mapped = db.ReportingCustomerClassChannels.AsNoTracking();
            lines = filter.Channel == OtherChannel
                ? lines.Where(line => !mapped.Any(channel => channel.CustomerClassId == line.CustomerClassId && channel.Channel != OtherChannel))
                : lines.Where(line => mapped.Any(channel => channel.CustomerClassId == line.CustomerClassId && channel.Channel == filter.Channel));
        }

        return lines;
    }

    private async Task<Node> TreeAsync(SalesReportView view, SalesReportFilter filter, IReadOnlyList<SalesReportColumn> columns, CancellationToken cancellationToken)
    {
        var lines = Filtered(filter);
        var quantity = filter.Measure == SalesReportMeasure.Quantity;
        var flat = view switch
        {
            SalesReportView.ByRep => lines.Select(line => new Flat
            {
                K0 = line.SalesRepId ?? "", L0 = line.SalesRepName ?? line.SalesRepId ?? NoRep,
                K1 = line.CustomerClassId ?? "", L1 = line.CustomerClassName ?? line.CustomerClassId ?? NoGroup,
                K2 = line.CustomerId, L2 = line.CustomerName ?? line.CustomerId,
                Year = line.FinancialYear, Month = line.FinancialMonth, Value = quantity ? line.Quantity : line.Amount,
            }),
            SalesReportView.ByProduct => lines.Select(line => new Flat
            {
                K0 = line.ItemClassId ?? "", L0 = line.ItemClassName ?? line.ItemClassId ?? NoGroup,
                K1 = line.InventoryId, L1 = line.ItemDescription ?? line.InventoryId,
                K2 = line.CustomerClassId ?? "", L2 = line.CustomerClassName ?? line.CustomerClassId ?? NoGroup,
                Year = line.FinancialYear, Month = line.FinancialMonth, Value = quantity ? line.Quantity : line.Amount,
            }),
            SalesReportView.ByProductThenRep => lines.Select(line => new Flat
            {
                K0 = line.ItemClassId ?? "", L0 = line.ItemClassName ?? line.ItemClassId ?? NoGroup,
                K1 = line.InventoryId, L1 = line.ItemDescription ?? line.InventoryId,
                K2 = line.SalesRepId ?? "", L2 = line.SalesRepName ?? line.SalesRepId ?? NoRep,
                Year = line.FinancialYear, Month = line.FinancialMonth, Value = quantity ? line.Quantity : line.Amount,
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(view), view, null),
        };

        var groups = await flat
            .GroupBy(row => new { row.K0, row.L0, row.K1, row.L1, row.K2, row.L2, row.Year, row.Month })
            .Select(group => new { group.Key.K0, group.Key.L0, group.Key.K1, group.Key.L1, group.Key.K2, group.Key.L2, group.Key.Year, group.Key.Month, Value = group.Sum(row => row.Value) })
            .ToListAsync(cancellationToken);

        var index = ColumnIndex(columns);
        var root = new Node(-1, string.Empty, string.Empty, columns.Count);
        foreach (var group in groups)
        {
            if (!index.TryGetValue((group.Year, group.Month), out var column))
            {
                continue;
            }

            var level0 = root.Child(group.K0, group.L0);
            var level1 = level0.Child(group.K1, group.L1);
            var level2 = level1.Child(group.K2, group.L2);
            root.Values[column] += group.Value;
            level0.Values[column] += group.Value;
            level1.Values[column] += group.Value;
            level2.Values[column] += group.Value;
        }

        return root;
    }

    private async Task<SalesReportGrid> CustomersAsync(SalesReportFilter filter, IReadOnlyList<SalesReportColumn> columns, DateTimeOffset? dataTo, CancellationToken cancellationToken)
    {
        var lines = Filtered(filter);
        var cells = await lines
            .GroupBy(line => new { Key = line.SalesRepId ?? "", Label = line.SalesRepName ?? line.SalesRepId ?? NoRep, line.FinancialYear, line.FinancialMonth })
            .Select(group => new { group.Key.Key, group.Key.Label, group.Key.FinancialYear, group.Key.FinancialMonth, Count = group.Select(line => line.CustomerId).Distinct().Count() })
            .ToListAsync(cancellationToken);
        var repTotals = await lines
            .GroupBy(line => line.SalesRepId ?? "")
            .Select(group => new { group.Key, Count = group.Select(line => line.CustomerId).Distinct().Count() })
            .ToDictionaryAsync(item => item.Key, item => item.Count, cancellationToken);
        var monthTotals = await lines
            .GroupBy(line => new { line.FinancialYear, line.FinancialMonth })
            .Select(group => new { group.Key.FinancialYear, group.Key.FinancialMonth, Count = group.Select(line => line.CustomerId).Distinct().Count() })
            .ToListAsync(cancellationToken);
        var grandTotal = await lines.Select(line => line.CustomerId).Distinct().CountAsync(cancellationToken);

        var index = ColumnIndex(columns);
        var rows = cells
            .GroupBy(cell => new { cell.Key, cell.Label })
            .OrderBy(group => group.Key.Label, StringComparer.Ordinal)
            .Select(group =>
            {
                var values = new decimal[columns.Count];
                foreach (var cell in group)
                {
                    if (index.TryGetValue((cell.FinancialYear, cell.FinancialMonth), out var column))
                    {
                        values[column] = cell.Count;
                    }
                }

                return new SalesReportRow(0, group.Key.Key, group.Key.Label, values, repTotals.GetValueOrDefault(group.Key.Key), false);
            })
            .ToList();
        var totals = new decimal[columns.Count];
        foreach (var month in monthTotals)
        {
            if (index.TryGetValue((month.FinancialYear, month.FinancialMonth), out var column))
            {
                totals[column] = month.Count;
            }
        }

        return new SalesReportGrid(["Rep"], columns, rows, new SalesReportTotals(totals, grandTotal), dataTo);
    }

    private async Task<DateTimeOffset?> DataToAsync(CancellationToken cancellationToken) =>
        await db.IntegrationRuns.AsNoTracking()
            .Where(run => run.RunType == SellInSyncService.RunType && run.Status == IntegrationRunStatuses.Succeeded)
            .MaxAsync(run => run.CompletedAt, cancellationToken);

    private static IReadOnlyList<SalesReportColumn> Columns(IReadOnlyList<int> years) =>
        years.Distinct().Order()
            .SelectMany(year => Enumerable.Range(1, 12).Select(month => new SalesReportColumn(year, month, Label(year, month))))
            .ToList();

    private static Dictionary<(int Year, int Month), int> ColumnIndex(IReadOnlyList<SalesReportColumn> columns) =>
        columns.Select((column, index) => (column, index)).ToDictionary(item => (item.column.FinancialYear, item.column.Month), item => item.index);

    private static string Label(int financialYear, int month) =>
        FinancialMonth.Create(financialYear, month).FirstDay.ToString("MMM yyyy", CultureInfo.InvariantCulture);

    private static IReadOnlyList<string> LevelNames(SalesReportView view) => view switch
    {
        SalesReportView.ByRep => ["Rep", "Customer group", "Customer"],
        SalesReportView.ByProduct => ["Item group", "Product", "Customer group"],
        SalesReportView.ByProductThenRep => ["Item group", "Product", "Rep"],
        _ => ["Rep"],
    };

    private static void Emit(Node node, HashSet<string> open, List<SalesReportRow> rows)
    {
        foreach (var child in node.Sorted())
        {
            rows.Add(new SalesReportRow(child.Level, child.Key, child.Label, child.Values, child.Values.Sum(), child.Children.Count > 0));
            if (open.Contains(child.Key))
            {
                Emit(child, open, rows);
            }
        }
    }

    private static string Number(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Csv(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;

    private sealed class Flat
    {
        public required string K0 { get; init; }
        public required string L0 { get; init; }
        public required string K1 { get; init; }
        public required string L1 { get; init; }
        public required string K2 { get; init; }
        public required string L2 { get; init; }
        public int Year { get; init; }
        public int Month { get; init; }
        public decimal Value { get; init; }
    }

    private sealed class Node(int level, string key, string label, int columns)
    {
        public int Level { get; } = level;
        public string Key { get; } = key;
        public string Label { get; } = label;
        public decimal[] Values { get; } = new decimal[columns];
        public Dictionary<string, Node> Children { get; } = new(StringComparer.Ordinal);

        public Node Child(string key, string label)
        {
            if (!Children.TryGetValue(key, out var child))
            {
                child = new Node(Level + 1, Level < 0 ? key : $"{Key}|{key}", label, Values.Length);
                Children[key] = child;
            }

            return child;
        }

        // The workbook pivots sort each level by name.
        public IEnumerable<Node> Sorted() => Children.Values.OrderBy(child => child.Label, StringComparer.OrdinalIgnoreCase);
    }
}
