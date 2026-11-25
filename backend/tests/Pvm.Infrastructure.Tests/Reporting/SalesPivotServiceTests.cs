using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Pvm.Application.Messaging;
using Pvm.Domain.Reporting;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Pvm.Infrastructure.Reporting;
using Pvm.Infrastructure.Reporting.SalesReport;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Reporting;

public sealed class SalesPivotServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<SalesDimension, IReadOnlyList<string>> All = SalesPivotFilter.All;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        // Made-up data: 2 reps, 2 customer classes, 3 customers, 2 products, 1 product with no class.
        db.ReportingProducts.AddRange(
            new ReportingProductEntity { InventoryId = "BAR01", Description = "Example bar", ItemClassId = "BARS", ItemClassName = "Bars", UpdatedAt = Now },
            new ReportingProductEntity { InventoryId = "GEL01", Description = "Example gel", ItemClassId = "GELS", ItemClassName = "Gels", UpdatedAt = Now },
            new ReportingProductEntity { InventoryId = "PROMO", Description = "Promo item", UpdatedAt = Now });
        db.ReportingCustomers.AddRange(
            new ReportingCustomerEntity { CustomerId = "C1", Name = "STORE ONE", CustomerClassId = "RETAIL", CustomerClassName = "Retail", Province = "GP", CurrentSalesRepId = "R1", UpdatedAt = Now },
            new ReportingCustomerEntity { CustomerId = "C2", Name = "STORE TWO", CustomerClassId = "RETAIL", CustomerClassName = "Retail", Province = "WC", CurrentSalesRepId = "R1", UpdatedAt = Now },
            new ReportingCustomerEntity { CustomerId = "P1", Name = "PHARMACY ONE", CustomerClassId = "PHARM", CustomerClassName = "Pharmacy", Province = "GP", CurrentSalesRepId = "R2", UpdatedAt = Now });
        db.ReportingSalesReps.AddRange(
            new ReportingSalesRepEntity { SalesRepId = "R1", Name = "Rep One", UpdatedAt = Now },
            new ReportingSalesRepEntity { SalesRepId = "R2", Name = "Rep Two", UpdatedAt = Now });
        db.ReportingCustomerClassChannels.Add(new ReportingCustomerClassChannelEntity { CustomerClassId = "PHARM", Channel = "Pharmacy", UpdatedBy = "test", UpdatedAt = Now });
        db.SellInLines.AddRange(
            Line("C1", "BAR01", 2027, 1, 100m, 10m),
            Line("C1", "BAR01", 2027, 2, 50m, 5m),
            Line("C2", "GEL01", 2027, 1, 30m, 3m),
            Line("C1", "BAR01", 2026, 1, 80m, 8m),
            Line("P1", "BAR01", 2027, 1, 200m, 20m),
            Line("C1", "PROMO", 2027, 1, 999m, 99m),
            Line("C1", "BAR01", 2027, 1, -10m, -1m, SellInDocumentType.CreditMemo));
        await db.SaveChangesAsync();
        await RefreshSummariesAsync(db);
    }

    // The sell-in load refreshes the summaries. The tests add lines themselves, so they refresh too.
    private static Task RefreshSummariesAsync(PvmDbContext db) => db.Database.ExecuteSqlRawAsync(SellInReportViews.RefreshSummaries);

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private static int _reference;

    private static SellInLineEntity Line(string customer, string item, int year, int month, decimal amount, decimal quantity, SellInDocumentType type = SellInDocumentType.Invoice) => new()
    {
        Id = Guid.NewGuid(),
        DocumentType = type,
        ReferenceNumber = $"DOC{Interlocked.Increment(ref _reference):00000}",
        LineNumber = 1,
        DocumentDate = FinancialMonth.Create(year, month).FirstDay.AddDays(5),
        FinancialYear = year,
        FinancialMonth = month,
        PostPeriod = $"{year:0000}{month:00}",
        CustomerId = customer,
        SalesRepId = "R9",
        InventoryId = item,
        Quantity = quantity,
        BaseQuantity = quantity,
        Amount = amount,
        LoadedAt = Now,
    };

    private static SalesPivotFilter Year2027(IReadOnlyDictionary<SalesDimension, IReadOnlyList<string>>? include = null, SalesPivotMeasure measure = SalesPivotMeasure.Amount) =>
        new(202701, 202712, include ?? All, measure);

    private static SalesPivotRequest Request(SalesPivotFilter filter, SalesDimension[] rows, string[]? open = null, SalesPivotColumns columns = SalesPivotColumns.Months, bool compare = false) =>
        new(filter, rows, columns, compare, open ?? []);

    [Fact]
    public async Task The_first_level_has_one_row_for_each_value_the_largest_first()
    {
        await using var db = CreateDbContext();

        var pivot = await Service(db).PivotAsync(
            Request(Year2027(), [SalesDimension.Rep, SalesDimension.Customer, SalesDimension.Product]), CancellationToken.None);

        Assert.Equal(12, pivot.Columns.Count);
        Assert.Equal((202701, "Mar 2026"), (pivot.Columns[0].Key, pivot.Columns[0].Label));
        Assert.Equal("Feb 2027", pivot.Columns[^1].Label);
        Assert.Equal([(0, "R2", "Rep Two", 200m), (0, "R1", "Rep One", 170m)], pivot.Rows.Select(row => (row.Level, row.Key, row.Label, row.Total)));
        Assert.Equal(120m, pivot.Rows[1].Values[0]); // 100 + 30 - 10. The item with no class is left out.
        Assert.Equal(50m, pivot.Rows[1].Values[1]);
        Assert.True(pivot.Rows[1].HasChildren);
        Assert.Null(pivot.Rows[1].Previous);
        Assert.Equal(370m, pivot.Totals.Total);
        Assert.Equal(320m, pivot.Totals.Values[0]);
    }

    [Fact]
    public async Task The_rows_drill_down_in_any_order()
    {
        await using var db = CreateDbContext();

        var pivot = await Service(db).PivotAsync(
            Request(Year2027(), [SalesDimension.Product, SalesDimension.Rep, SalesDimension.Customer], ["BAR01", "BAR01|R1"]), CancellationToken.None);

        Assert.Equal(
            [(0, "BAR01", "Example bar", 340m), (1, "BAR01|R2", "Rep Two", 200m), (1, "BAR01|R1", "Rep One", 140m),
             (2, "BAR01|R1|C1", "STORE ONE", 140m), (0, "GEL01", "Example gel", 30m)],
            pivot.Rows.Select(row => (row.Level, row.Key, row.Label, row.Total)));
        Assert.False(pivot.Rows[3].HasChildren);
    }

    [Fact]
    public async Task Rows_sort_by_label_on_request()
    {
        await using var db = CreateDbContext();

        var pivot = await Service(db).PivotAsync(
            Request(Year2027(), [SalesDimension.Rep]) with { SortByLabel = true }, CancellationToken.None);

        Assert.Equal(["Rep One", "Rep Two"], pivot.Rows.Select(row => row.Label));
    }

    [Fact]
    public async Task Each_dimension_keeps_the_selected_values_and_the_dimensions_combine()
    {
        await using var db = CreateDbContext();
        var service = Service(db);

        async Task<decimal> Total(Dictionary<SalesDimension, IReadOnlyList<string>> include) =>
            (await service.PivotAsync(Request(Year2027(include), [SalesDimension.Rep]), CancellationToken.None)).Totals.Total;

        Assert.Equal(140m, await Total(new() { [SalesDimension.Rep] = ["R1"], [SalesDimension.Customer] = ["C1", "P1"] }));
        Assert.Equal(340m, await Total(new() { [SalesDimension.Province] = ["GP"] }));
        Assert.Equal(170m, await Total(new() { [SalesDimension.Channel] = ["Other"] }));
        Assert.Equal(370m, await Total(new() { [SalesDimension.Channel] = ["Pharmacy", "Other"] }));
        Assert.Equal(30m, await Total(new() { [SalesDimension.ItemGroup] = ["GELS"], [SalesDimension.CustomerGroup] = ["RETAIL"] }));
        Assert.Equal(0m, await Total(new() { [SalesDimension.Rep] = ["R2"], [SalesDimension.Product] = ["GEL01"] }));
    }

    [Fact]
    public async Task A_comparison_gives_the_same_months_a_year_before()
    {
        await using var db = CreateDbContext();

        var pivot = await Service(db).PivotAsync(
            Request(new SalesPivotFilter(202701, 202702, All), [SalesDimension.Rep], compare: true), CancellationToken.None);

        var repOne = pivot.Rows.Single(row => row.Key == "R1");
        Assert.Equal([120m, 50m], repOne.Values);
        Assert.Equal([80m, 0m], repOne.Previous!);
        Assert.Equal(80m, repOne.PreviousTotal);
        Assert.Equal(0m, pivot.Rows.Single(row => row.Key == "R2").PreviousTotal);
        Assert.Equal(80m, pivot.Totals.PreviousTotal);
        Assert.Equal(370m, pivot.Totals.Total);
    }

    [Fact]
    public async Task The_customers_measure_counts_distinct_customers_at_each_level()
    {
        await using var db = CreateDbContext();

        var pivot = await Service(db).PivotAsync(
            Request(Year2027(measure: SalesPivotMeasure.Customers), [SalesDimension.Rep]), CancellationToken.None);

        var repOne = pivot.Rows.Single(row => row.Key == "R1");
        Assert.Equal(2m, repOne.Total);
        Assert.Equal(2m, repOne.Values[0]);
        Assert.Equal(1m, repOne.Values[1]);
        Assert.Equal(3m, pivot.Totals.Total);
        Assert.Equal(3m, pivot.Totals.Values[0]);
    }

    [Fact]
    public async Task Columns_can_be_years_or_none()
    {
        await using var db = CreateDbContext();
        var service = Service(db);

        var years = await service.PivotAsync(
            Request(new SalesPivotFilter(202601, 202712, All), [SalesDimension.Rep], columns: SalesPivotColumns.Years), CancellationToken.None);
        var none = await service.PivotAsync(Request(Year2027(), [SalesDimension.Rep], columns: SalesPivotColumns.None), CancellationToken.None);

        Assert.Equal(["FY2026", "FY2027"], years.Columns.Select(column => column.Label));
        Assert.Equal([80m, 170m], years.Rows.Single(row => row.Key == "R1").Values);
        Assert.Empty(none.Columns);
        Assert.Equal(370m, none.Totals.Total);
    }

    [Fact]
    public async Task The_values_of_a_dimension_ignore_its_own_selection_but_keep_the_others()
    {
        await using var db = CreateDbContext();
        var service = Service(db);
        var filter = Year2027(new Dictionary<SalesDimension, IReadOnlyList<string>> { [SalesDimension.Customer] = ["C1"], [SalesDimension.Rep] = ["R1"] });

        var customers = await service.ValuesAsync(SalesDimension.Customer, filter, null, 100, CancellationToken.None);
        var found = await service.ValuesAsync(SalesDimension.Customer, filter, " two", 100, CancellationToken.None);

        Assert.Equal([("C1", "STORE ONE", 140m), ("C2", "STORE TWO", 30m)], customers.Select(value => (value.Key, value.Label, value.Total)));
        Assert.Equal(["C2"], found.Select(value => value.Key));
    }

    [Fact]
    public async Task The_labels_of_selected_keys_come_in_any_month()
    {
        await using var db = CreateDbContext();

        var labels = await Service(db).LabelsAsync(SalesDimension.Customer, ["P1", "C1", "NONE"], CancellationToken.None);

        Assert.Equal([("P1", "PHARMACY ONE"), ("C1", "STORE ONE")], labels.Select(value => (value.Key, value.Label)));
    }

    [Fact]
    public async Task A_null_open_gives_every_level()
    {
        await using var db = CreateDbContext();

        var pivot = await Service(db).PivotAsync(Request(Year2027(), [SalesDimension.Rep, SalesDimension.Customer]) with { Open = null }, CancellationToken.None);

        Assert.Equal(
            [(0, "R2"), (1, "R2|P1"), (0, "R1"), (1, "R1|C1"), (1, "R1|C2")],
            pivot.Rows.Select(row => (row.Level, row.Key)));
    }

    [Fact]
    public async Task A_whole_tree_in_one_read_matches_the_tree_level_by_level()
    {
        await using var db = CreateDbContext();
        var service = Service(db);
        var rows = new[] { SalesDimension.ItemGroup, SalesDimension.Product, SalesDimension.Customer };

        var tree = await service.PivotAsync(Request(Year2027(), rows, columns: SalesPivotColumns.Months, compare: true) with { Open = null }, CancellationToken.None);
        var opened = await service.PivotAsync(
            Request(Year2027(), rows, ["BARS", "GELS", "BARS|BAR01", "GELS|GEL01"], SalesPivotColumns.Months, compare: true), CancellationToken.None);

        Assert.Equal(
            opened.Rows.Select(row => (row.Level, row.Key, row.Label, row.Total, row.PreviousTotal, string.Join(",", row.Values))),
            tree.Rows.Select(row => (row.Level, row.Key, row.Label, row.Total, row.PreviousTotal, string.Join(",", row.Values))));
        Assert.Equal(opened.Totals.Total, tree.Totals.Total);
        Assert.Equal(370m, tree.Totals.Total);
    }

    [Fact]
    public async Task A_second_request_comes_from_the_cache_until_the_next_load()
    {
        await using var db = CreateDbContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new SalesPivotService(db, cache);
        var request = Request(Year2027(), [SalesDimension.Rep]);
        var first = await service.PivotAsync(request, CancellationToken.None);

        db.SellInLines.Add(Line("P1", "BAR01", 2027, 2, 1000m, 1m));
        await db.SaveChangesAsync();
        await RefreshSummariesAsync(db);
        var cached = await service.PivotAsync(request, CancellationToken.None);

        db.IntegrationRuns.Add(new IntegrationRunEntity
        {
            Id = Guid.NewGuid(),
            RunType = SellInSyncService.RunType,
            Trigger = "manual",
            InitiatedBy = "test",
            EnvironmentName = "test",
            CorrelationId = "test",
            Status = IntegrationRunStatuses.Succeeded,
            CreatedAt = Now,
            UpdatedAt = Now,
            StartedAt = Now,
            CompletedAt = Now,
        });
        await db.SaveChangesAsync();
        cache.Remove("sales-data-to"); // The end of the last load stays a minute in the cache.
        var fresh = await service.PivotAsync(request, CancellationToken.None);

        Assert.Same(first, cached);
        Assert.Equal(370m, cached.Totals.Total);
        Assert.Equal(1370m, fresh.Totals.Total);
    }

    [Fact]
    public async Task The_workbook_is_an_xlsx_with_the_filters_on_top_and_one_open_row_for_each_item()
    {
        await using var db = CreateDbContext();
        var filter = new SalesPivotFilter(202701, 202702, new Dictionary<SalesDimension, IReadOnlyList<string>> { [SalesDimension.Rep] = ["R1"] });

        var bytes = await Service(db).WorkbookAsync(Request(filter, [SalesDimension.Rep, SalesDimension.Customer], compare: true), CancellationToken.None);

        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.Equal(
            ["[Content_Types].xml", "_rels/.rels", "xl/_rels/workbook.xml.rels", "xl/styles.xml", "xl/workbook.xml", "xl/worksheets/sheet1.xml"],
            zip.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
        foreach (var entry in zip.Entries)
        {
            using var stream = entry.Open();
            XDocument.Load(stream); // Each part is well-formed XML.
        }

        XNamespace x = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var sheetStream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var rows = XDocument.Load(sheetStream).Descendants(x + "row").ToList();
        string Text(XElement row) => string.Join("|", row.Elements(x + "c").Select(cell => (string?)cell.Element(x + "is")?.Element(x + "t") ?? (string?)cell.Element(x + "v") ?? ""));
        var texts = rows.Select(Text).ToList();
        Assert.Equal("Sales report by rep, then customer", texts[0]);
        Assert.Contains("Period: Mar 2026 to Apr 2026", texts);
        Assert.Contains("Compared with: Mar 2025 to Apr 2025", texts);
        Assert.Contains("Rep: Rep One", texts);
        Assert.Contains("Rep|Customer|Mar 2026|Apr 2026|Total|A year before|Change", texts);
        // Each customer has a row of its own, with its rep in the first column. No subtotal rows,
        // no closed groups: a person sorts and filters the sheet as it is.
        Assert.Contains(texts, text => text.StartsWith("Rep One|STORE ONE|90|50|140|", StringComparison.Ordinal));
        Assert.Contains(texts, text => text.StartsWith("Rep One|STORE TWO|30|0|30|", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, text => text.StartsWith("Rep One|120", StringComparison.Ordinal));
        Assert.All(rows, row => Assert.Null(row.Attribute("hidden")));
        Assert.All(rows, row => Assert.Null(row.Attribute("outlineLevel")));
        Assert.Equal("Total|120|50|170|80|1.125", texts[^1]);
    }

    [Theory]
    [InlineData(1, "A")]
    [InlineData(26, "Z")]
    [InlineData(27, "AA")]
    [InlineData(53, "BA")]
    public void Excel_column_names_follow_the_alphabet(int column, string name)
    {
        Assert.Equal(name, SalesPivotWorkbook.ColumnName(column));
    }

    [Fact]
    public async Task The_csv_has_every_level_and_a_total()
    {
        await using var db = CreateDbContext();

        var csv = await Service(db).CsvAsync(
            Request(new SalesPivotFilter(202701, 202702, All), [SalesDimension.Rep, SalesDimension.Customer]), CancellationToken.None);

        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Level,Rep,Customer,Mar 2026,Apr 2026,Total", lines[0]);
        Assert.Equal(
            ["0,Rep Two,,200.00,0.00,200.00", "1,Rep Two,PHARMACY ONE,200.00,0.00,200.00", "0,Rep One,,120.00,50.00,170.00",
             "1,Rep One,STORE ONE,90.00,50.00,140.00", "1,Rep One,STORE TWO,30.00,0.00,30.00", "Total,,,320.00,50.00,370.00"],
            lines[1..]);
    }

    [Fact]
    public async Task The_lines_follow_the_filter()
    {
        await using var db = CreateDbContext();

        var page = await Service(db).LinesAsync(
            Year2027(new Dictionary<SalesDimension, IReadOnlyList<string>> { [SalesDimension.Rep] = ["R2"] }), 1, 100, CancellationToken.None);

        Assert.Equal(1, page.TotalLines);
        Assert.Equal(("P1", "PHARMACY ONE", "Rep Two", 200m), (page.Lines[0].CustomerId, page.Lines[0].CustomerName, page.Lines[0].Rep, page.Lines[0].Amount));
    }

    [Fact]
    public async Task The_meta_gives_the_months_with_sales()
    {
        await using var db = CreateDbContext();

        var meta = await Service(db).MetaAsync(CancellationToken.None);

        Assert.Equal((202601, 202702), (meta.FirstMonth, meta.LastMonth));
    }

    [Fact]
    public async Task A_person_saves_recalls_changes_and_deletes_named_filters()
    {
        await using var db = CreateDbContext();
        var service = new SalesReportSavedFilterService(db, TimeProvider.System);
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();

        var (created, mine) = await service.CreateAsync(owner, "Owner", " Gauteng pharmacies ", "?rows=rep,customer&province=GP", false, CancellationToken.None);
        var (again, _) = await service.CreateAsync(owner, "Owner", "Gauteng pharmacies", "rows=rep", false, CancellationToken.None);
        var (shared, sharedFilter) = await service.CreateAsync(other, "Other", "All reps", "rows=rep", true, CancellationToken.None);
        await service.CreateAsync(other, "Other", "Private", "rows=rep", false, CancellationToken.None);

        Assert.Equal(SavedFilterResult.Done, created);
        Assert.Equal(("Gauteng pharmacies", "rows=rep,customer&province=GP", true), (mine!.Name, mine.Query, mine.Mine));
        Assert.Equal(SavedFilterResult.NameInUse, again);
        Assert.Equal(SavedFilterResult.Done, shared);
        Assert.Equal([("Gauteng pharmacies", true), ("All reps", false)], (await service.ListAsync(owner, CancellationToken.None)).Select(filter => (filter.Name, filter.Mine)));

        var (renamed, after) = await service.UpdateAsync(mine.Id, owner, "GP pharmacies", null, true, CancellationToken.None);
        Assert.Equal(SavedFilterResult.Done, renamed);
        Assert.Equal(("GP pharmacies", "rows=rep,customer&province=GP", true), (after!.Name, after.Query, after.Shared));
        Assert.Equal(SavedFilterResult.NotOwner, (await service.UpdateAsync(sharedFilter!.Id, owner, "Mine now", null, null, CancellationToken.None)).Result);
        Assert.Equal(SavedFilterResult.Invalid, (await service.UpdateAsync(mine.Id, owner, " ", null, null, CancellationToken.None)).Result);
        Assert.Equal(SavedFilterResult.NotOwner, await service.DeleteAsync(sharedFilter.Id, owner, CancellationToken.None));
        Assert.Equal(SavedFilterResult.Done, await service.DeleteAsync(mine.Id, owner, CancellationToken.None));
        Assert.Equal(SavedFilterResult.NotFound, await service.DeleteAsync(mine.Id, owner, CancellationToken.None));
    }

    private static SalesPivotService Service(PvmDbContext db) => new(db, new MemoryCache(new MemoryCacheOptions()));

    private PvmDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<PvmDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);
}
