using Microsoft.EntityFrameworkCore;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Persistence;

public sealed class MerchandiserSettingsPersistenceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task A_company_with_its_areas_lines_and_makro_stores_round_trips()
    {
        var company = NewCompany("Example Merchandising");
        var area = NewArea(company.Id, "Example Area", order: 1, rate: 5m);
        var line = new MerchandiserLineEntity
        {
            Id = Guid.NewGuid(),
            AreaId = area.Id,
            Label = "Example Retail",
            Order = 1,
            CustomerClassId = "RETAIL",
            SalesRepIds = ["REP01", "REP02"],
            CustomerIds = [],
        };
        var store = new RetailStoreEntity
        {
            Id = Guid.NewGuid(), Retailer = "makro", StoreCode = "M01", Name = "Example Main Store", CreatedAt = Now, UpdatedAt = Now,
        };

        await using (var db = CreateDbContext())
        {
            db.RetailStores.Add(store);
            db.MerchandiserCompanies.Add(company);
            db.MerchandiserAreas.Add(area);
            db.MerchandiserLines.Add(line);
            db.MerchandiserMakroStores.Add(new MerchandiserMakroStoreEntity { AreaId = area.Id, StoreId = store.Id });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext())
        {
            var stored = await db.MerchandiserCompanies.SingleAsync();
            Assert.Equal(MerchandiserPaymentKinds.Commission, stored.PaymentKind);
            Assert.Equal(5m, stored.PaymentRatePercent);
            Assert.Equal(new DateOnly(2026, 10, 1), stored.ValidFrom);
            Assert.Equal(5m, (await db.MerchandiserAreas.SingleAsync()).CommissionRatePercent);
            var storedLine = await db.MerchandiserLines.SingleAsync();
            Assert.Equal(["REP01", "REP02"], storedLine.SalesRepIds);
            Assert.Empty(storedLine.CustomerIds);
            Assert.Equal(store.Id, (await db.MerchandiserMakroStores.SingleAsync()).StoreId);
        }
    }

    [Fact]
    public async Task A_company_name_is_unique_for_each_valid_from_month()
    {
        await using var db = CreateDbContext();
        db.MerchandiserCompanies.Add(NewCompany("Example Merchandising"));
        db.MerchandiserCompanies.Add(NewCompany("Example Merchandising", validFrom: new DateOnly(2027, 3, 1)));
        await db.SaveChangesAsync();

        db.MerchandiserCompanies.Add(NewCompany("Example Merchandising"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_makro_store_belongs_to_one_area_of_a_company_only_once()
    {
        var company = NewCompany("Example Merchandising");
        var area = NewArea(company.Id, "Example Area", order: 1, rate: null);
        var store = new RetailStoreEntity
        {
            Id = Guid.NewGuid(), Retailer = "makro", StoreCode = "M01", Name = "Example Main Store", CreatedAt = Now, UpdatedAt = Now,
        };
        await using (var db = CreateDbContext())
        {
            db.AddRange(company, area, store, new MerchandiserMakroStoreEntity { AreaId = area.Id, StoreId = store.Id });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext())
        {
            db.MerchandiserMakroStores.Add(new MerchandiserMakroStoreEntity { AreaId = area.Id, StoreId = store.Id });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Deleting_a_company_deletes_its_areas_lines_and_store_links()
    {
        var company = NewCompany("Example Merchandising");
        var area = NewArea(company.Id, "Example Area", order: 1, rate: null);
        await using (var db = CreateDbContext())
        {
            db.AddRange(company, area, new MerchandiserLineEntity
            {
                Id = Guid.NewGuid(), AreaId = area.Id, Label = "Line", Order = 1, CustomerClassId = "RETAIL", SalesRepIds = [], CustomerIds = [],
            });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext())
        {
            db.MerchandiserCompanies.Remove(await db.MerchandiserCompanies.SingleAsync());
            await db.SaveChangesAsync();
            Assert.Empty(await db.MerchandiserAreas.ToListAsync());
            Assert.Empty(await db.MerchandiserLines.ToListAsync());
        }
    }

    private static MerchandiserCompanyEntity NewCompany(string name, DateOnly? validFrom = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        StatementTitle = name.ToUpperInvariant(),
        PaymentKind = MerchandiserPaymentKinds.Commission,
        PaymentRatePercent = 5m,
        ShowDisChemYearOnYear = false,
        ValidFrom = validFrom ?? new DateOnly(2026, 10, 1),
        UpdatedBy = "test",
        UpdatedAt = Now,
    };

    private static MerchandiserAreaEntity NewArea(Guid companyId, string name, int order, decimal? rate) => new()
    {
        Id = Guid.NewGuid(), CompanyId = companyId, Name = name, Order = order, CommissionRatePercent = rate,
    };

    private PvmDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<PvmDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);
}
