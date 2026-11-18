using Microsoft.EntityFrameworkCore;
using Pvm.Domain.Reporting;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Pvm.Infrastructure.Reporting.SalesReport;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Reporting;

public sealed class SalesReportServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

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
    }

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

    private SalesReportService Service(PvmDbContext db) => new(db);

    [Fact]
    public async Task By_rep_gives_one_row_for_each_rep_with_months_of_each_year_and_totals()
    {
        await using var db = CreateDbContext();

        var grid = await Service(db).GridAsync(SalesReportView.ByRep, new SalesReportFilter([2026, 2027]), [], CancellationToken.None);

        Assert.Equal(24, grid.Columns.Count);
        Assert.Equal((2026, 1, "Mar 2025"), (grid.Columns[0].FinancialYear, grid.Columns[0].Month, grid.Columns[0].Label));
        Assert.Equal((2027, 12, "Feb 2027"), (grid.Columns[^1].FinancialYear, grid.Columns[^1].Month, grid.Columns[^1].Label));
        Assert.Equal(["Rep One", "Rep Two"], grid.Rows.Select(row => row.Label));
        Assert.All(grid.Rows, row => Assert.Equal(0, row.Level));
        var repOne = grid.Rows[0];
        Assert.Equal(250m, repOne.Total); // 100 + 50 + 30 + 80 - 10: the item with no class is left out.
        Assert.Equal(80m, repOne.Values[0]);
        Assert.Equal(120m, repOne.Values[12]); // 100 + 30 - 10 in month 1 of 2027.
        Assert.True(repOne.HasChildren);
        Assert.Equal(450m, grid.Totals.Total);
    }

    [Fact]
    public async Task An_open_rep_shows_its_customer_groups_and_an_open_group_its_customers()
    {
        await using var db = CreateDbContext();

        var grid = await Service(db).GridAsync(SalesReportView.ByRep, new SalesReportFilter([2027]), ["R1", "R1|RETAIL"], CancellationToken.None);

        Assert.Equal(
            [(0, "Rep One"), (1, "Retail"), (2, "STORE ONE"), (2, "STORE TWO"), (0, "Rep Two")],
            grid.Rows.Select(row => (row.Level, row.Label)));
        Assert.Equal("R1|RETAIL|C1", grid.Rows[2].Key);
        Assert.False(grid.Rows[2].HasChildren);
    }

    [Fact]
    public async Task Filters_narrow_the_lines_and_a_class_with_no_channel_is_other()
    {
        await using var db = CreateDbContext();
        var service = Service(db);

        var pharmacy = await service.GridAsync(SalesReportView.ByRep, new SalesReportFilter([2027], Channel: "Pharmacy"), [], CancellationToken.None);
        var other = await service.GridAsync(SalesReportView.ByRep, new SalesReportFilter([2027], Channel: "Other"), [], CancellationToken.None);
        var western = await service.GridAsync(SalesReportView.ByRep, new SalesReportFilter([2027], Province: "WC"), [], CancellationToken.None);
        var gels = await service.GridAsync(SalesReportView.ByRep, new SalesReportFilter([2027], ItemClassId: "GELS"), [], CancellationToken.None);

        Assert.Equal(200m, pharmacy.Totals.Total);
        Assert.Equal(170m, other.Totals.Total);
        Assert.Equal(30m, western.Totals.Total);
        Assert.Equal(30m, gels.Totals.Total);
        Assert.Equal(["Pharmacy", "Other"], await service.ChannelsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Quantity_is_a_measure_too()
    {
        await using var db = CreateDbContext();

        var grid = await Service(db).GridAsync(SalesReportView.ByRep, new SalesReportFilter([2027], Measure: SalesReportMeasure.Quantity), [], CancellationToken.None);

        Assert.Equal(37m, grid.Totals.Total); // 10 + 5 + 3 + 20 - 1.
    }

    [Fact]
    public async Task By_product_groups_item_class_then_product_then_customer_group_or_rep()
    {
        await using var db = CreateDbContext();
        var service = Service(db);

        var byGroup = await service.GridAsync(SalesReportView.ByProduct, new SalesReportFilter([2027]), ["BARS", "BARS|BAR01"], CancellationToken.None);
        var byRep = await service.GridAsync(SalesReportView.ByProductThenRep, new SalesReportFilter([2027]), ["BARS", "BARS|BAR01"], CancellationToken.None);

        Assert.Equal([(0, "Bars"), (1, "Example bar"), (2, "Pharmacy"), (2, "Retail"), (0, "Gels")], byGroup.Rows.Select(row => (row.Level, row.Label)));
        Assert.Equal([(0, "Bars"), (1, "Example bar"), (2, "Rep One"), (2, "Rep Two"), (0, "Gels")], byRep.Rows.Select(row => (row.Level, row.Label)));
    }

    [Fact]
    public async Task Customers_counts_distinct_customers_and_the_total_is_not_a_sum()
    {
        await using var db = CreateDbContext();

        var grid = await Service(db).GridAsync(SalesReportView.Customers, new SalesReportFilter([2027]), [], CancellationToken.None);

        var repOne = grid.Rows.Single(row => row.Label == "Rep One");
        Assert.Equal(2m, repOne.Values[0]); // C1 and C2 in month 1.
        Assert.Equal(1m, repOne.Values[1]); // C1 in month 2.
        Assert.Equal(2m, repOne.Total); // 2 customers in the year, not 3.
        Assert.Equal(3m, grid.Totals.Total);
        Assert.False(repOne.HasChildren);
    }

    [Fact]
    public async Task The_overview_gives_the_months_of_this_year_and_the_year_before()
    {
        await using var db = CreateDbContext();

        var overview = await Service(db).OverviewAsync(2027, new SalesReportFilter([2027]), CancellationToken.None);

        Assert.Equal(12, overview.Months.Count);
        Assert.Equal((320m, 80m), (overview.Months[0].ThisYear, overview.Months[0].LastYear));
        Assert.Equal(370m, overview.ThisYearTotal);
        Assert.Equal(3, overview.ActiveCustomers);
    }

    [Fact]
    public async Task The_csv_has_all_levels_with_a_header_of_months()
    {
        await using var db = CreateDbContext();

        var csv = await Service(db).CsvAsync(SalesReportView.ByRep, new SalesReportFilter([2027]), CancellationToken.None);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("Level,Rep,Customer group,Customer,Mar 2026,Apr 2026", lines[0], StringComparison.Ordinal);
        Assert.EndsWith(",Total\r", lines[0], StringComparison.Ordinal);
        Assert.Contains(lines, line => line.StartsWith("2,Rep One,Retail,STORE ONE,90.00,50.00", StringComparison.Ordinal));
        Assert.StartsWith("Total,,,,", lines[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_data_list_gives_lines_a_page_at_a_time()
    {
        await using var db = CreateDbContext();

        var page = await Service(db).LinesAsync(new SalesReportFilter([2027]), page: 1, pageSize: 2, CancellationToken.None);

        Assert.Equal(5, page.TotalLines);
        Assert.Equal(2, page.Lines.Count);
    }

    private PvmDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<PvmDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);
}
