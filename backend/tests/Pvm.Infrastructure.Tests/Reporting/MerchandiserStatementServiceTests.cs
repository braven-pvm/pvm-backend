using Microsoft.EntityFrameworkCore;
using Pvm.Domain.Reporting;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Pvm.Infrastructure.Reporting.SellOut;
using Pvm.Infrastructure.Reporting.Statements;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Reporting;

public sealed class MerchandiserStatementServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly October = new(2026, 10, 1);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();
    private readonly Guid _store = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        // Made-up sell-in: two customers of the class RETAIL with reps R1 and R2, and one item.
        db.ReportingProducts.Add(new ReportingProductEntity { InventoryId = "ITEM01", Description = "Example bar", ItemClassId = "BARS", UpdatedAt = Now });
        db.ReportingCustomers.AddRange(
            new ReportingCustomerEntity { CustomerId = "C1", Name = "DIS-CHEM PHARMACIES LTD EXAMPLE S001", CustomerClassId = "RETAIL", CurrentSalesRepId = "R1", UpdatedAt = Now },
            new ReportingCustomerEntity { CustomerId = "C2", Name = "OTHER STORE", CustomerClassId = "RETAIL", CurrentSalesRepId = "R2", UpdatedAt = Now });
        db.SellInLines.AddRange(
            Line("C1", "IN1", 100m, October),
            Line("C1", "IN2", 50m, October),
            Line("C2", "IN3", 999m, October),
            Line("C1", "IN4", 999m, October.AddMonths(-1)));

        // Made-up Makro value of one store.
        db.RetailStores.Add(new RetailStoreEntity { Id = _store, Retailer = "makro", StoreCode = "M01", Name = "Example Main Store", CreatedAt = Now, UpdatedAt = Now });
        var import = Guid.NewGuid();
        db.SellOutMonths.Add(new SellOutMonthEntity
        {
            Id = Guid.NewGuid(), ImportId = import, Source = MakroDispoParser.SourceName, StoreId = _store, SourceProductCode = "100000001",
            Year = 2026, Month = 10, FinancialYear = 2027, FinancialMonth = 8, Units = 6m, UnitsEach = 6m, ValueExclVat = 60m,
        });

        var company = new MerchandiserCompanyEntity
        {
            Id = Guid.NewGuid(), Name = "Example", StatementTitle = "EXAMPLE", PaymentKind = MerchandiserPaymentKinds.Commission,
            PaymentRatePercent = 5m, ValidFrom = new DateOnly(2026, 3, 1), UpdatedBy = "test", UpdatedAt = Now,
        };
        var area = new MerchandiserAreaEntity { Id = Guid.NewGuid(), CompanyId = company.Id, Name = "North", Order = 1, CommissionRatePercent = 5m };
        db.MerchandiserCompanies.Add(company);
        db.MerchandiserAreas.Add(area);
        db.MerchandiserLines.Add(new MerchandiserLineEntity
        {
            Id = Guid.NewGuid(), AreaId = area.Id, Label = "Retail", Order = 1, CustomerClassId = "RETAIL", SalesRepIds = ["R1"], CustomerIds = [],
        });
        db.MerchandiserMakroStores.Add(new MerchandiserMakroStoreEntity { AreaId = area.Id, StoreId = _store });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task A_statement_reads_the_sell_in_view_and_the_makro_values_of_the_month()
    {
        await using var db = CreateDbContext();

        var statement = await new MerchandiserStatementService(db).BuildAsync("Example", October, October, CancellationToken.None);

        var area = Assert.Single(statement!.Areas);
        var line = Assert.Single(area.Lines);
        Assert.Equal(150m, line.Amount);
        Assert.Equal("DIS-CHEM EXAMPLE S001", Assert.Single(line.Customers).Name);
        Assert.Equal(60m, area.MakroValue);
        Assert.Equal(210m, statement.GrandTotal);
        Assert.Equal(10.50m, statement.Commission);
    }

    [Fact]
    public async Task A_company_with_no_settings_for_the_month_gives_no_statement()
    {
        await using var db = CreateDbContext();

        Assert.Null(await new MerchandiserStatementService(db).BuildAsync("Example", new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1), CancellationToken.None));
        Assert.Null(await new MerchandiserStatementService(db).BuildAsync("Unknown", October, October, CancellationToken.None));
    }

    private static SellInLineEntity Line(string customer, string reference, decimal amount, DateOnly month)
    {
        var financial = FinancialMonth.FromCalendar(month.Year, month.Month);
        return new SellInLineEntity
        {
            Id = Guid.NewGuid(),
            DocumentType = SellInDocumentType.Invoice,
            ReferenceNumber = reference,
            LineNumber = 1,
            DocumentDate = month.AddDays(10),
            FinancialYear = financial.FinancialYear,
            FinancialMonth = financial.Number,
            PostPeriod = $"{financial.FinancialYear:0000}{financial.Number:00}",
            CustomerId = customer,
            SalesRepId = "R9",
            InventoryId = "ITEM01",
            Quantity = 1m,
            BaseQuantity = 1m,
            Amount = amount,
            LoadedAt = Now,
        };
    }

    private PvmDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<PvmDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);
}
