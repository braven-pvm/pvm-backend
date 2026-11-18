using Microsoft.EntityFrameworkCore;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Pvm.Infrastructure.Reporting.Statements;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Reporting;

public sealed class MerchandiserSettingsServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        // Made-up names and ids, as the loads keep them.
        db.ReportingSalesReps.AddRange(
            new ReportingSalesRepEntity { SalesRepId = "R01", Name = "Example Rep", UpdatedAt = Now },
            new ReportingSalesRepEntity { SalesRepId = "R02", Name = "Other Rep", UpdatedAt = Now });
        db.ReportingCustomers.AddRange(
            new ReportingCustomerEntity { CustomerId = "C1", Name = "STORE ONE", CustomerClassId = "PNPFAM", CustomerClassName = "P&P Family & Mini", UpdatedAt = Now },
            new ReportingCustomerEntity { CustomerId = "DC1", Name = "CHECKERS DC", CustomerClassId = "CHKGP", CustomerClassName = "Checker Gauteng", UpdatedAt = Now });
        db.RetailStores.Add(new RetailStoreEntity { Id = Guid.NewGuid(), Retailer = "makro", StoreCode = "M01", Name = "Example Main Store", CreatedAt = Now, UpdatedAt = Now });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private static MerchandiserSettingsDocument Document(string rep = "Example Rep", string makro = "Example Main Store") => new(
        "Example Merchandising",
        "EXAMPLE MERCHANDISING",
        "2026-10",
        "commission",
        5m,
        null,
        false,
        [
            new MerchandiserAreaDocument(
                "North",
                5m,
                [
                    new MerchandiserLineDocument("P&P Family", "P&P  Family & Mini", [rep, "R02"], [], false),
                    new MerchandiserLineDocument("Checkers DC", "Checker Gauteng", [], ["DC1"], false),
                ],
                [makro]),
        ]);

    [Fact]
    public async Task Names_resolve_to_ids_and_the_export_gives_the_names_back()
    {
        await using (var db = CreateDbContext())
        {
            var result = await Service(db).ImportAsync(Document(), "user:test", CancellationToken.None);
            Assert.True(result.Saved, string.Join("; ", result.Problems));
        }

        await using (var db = CreateDbContext())
        {
            var line = await db.MerchandiserLines.OrderBy(item => item.Order).FirstAsync();
            Assert.Equal("PNPFAM", line.CustomerClassId);
            Assert.Equal(["R01", "R02"], line.SalesRepIds);
            Assert.Single(await db.MerchandiserMakroStores.ToListAsync());

            var exported = await Service(db).ExportAsync("Example Merchandising", CancellationToken.None);
            var area = Assert.Single(exported!.Areas);
            Assert.Equal("P&P Family & Mini", area.Lines[0].CustomerGroup);
            Assert.Equal(["Example Rep", "Other Rep"], area.Lines[0].Reps);
            Assert.Equal(["DC1"], area.Lines[1].Customers);
            Assert.Equal(["Example Main Store"], area.MakroStores);
            Assert.Equal("2026-10", exported.ValidFrom);
        }
    }

    [Fact]
    public async Task An_unknown_name_saves_nothing_and_says_which_name()
    {
        await using var db = CreateDbContext();

        var result = await Service(db).ImportAsync(Document(rep: "Nobody", makro: "Unknown Store"), "user:test", CancellationToken.None);

        Assert.False(result.Saved);
        Assert.Contains(result.Problems, problem => problem.Contains("'Nobody'", StringComparison.Ordinal));
        Assert.Contains(result.Problems, problem => problem.Contains("'Unknown Store'", StringComparison.Ordinal));
        Assert.Empty(await db.MerchandiserCompanies.ToListAsync());
    }

    [Fact]
    public async Task A_second_import_of_the_same_month_replaces_the_version_and_another_month_adds_one()
    {
        await using (var db = CreateDbContext())
        {
            await Service(db).ImportAsync(Document(), "user:test", CancellationToken.None);
            await Service(db).ImportAsync(Document(), "user:test", CancellationToken.None);
            await Service(db).ImportAsync(Document() with { ValidFrom = "2027-03" }, "user:test", CancellationToken.None);
        }

        await using var check = CreateDbContext();
        Assert.Equal([new DateOnly(2026, 10, 1), new DateOnly(2027, 3, 1)], await check.MerchandiserCompanies.OrderBy(item => item.ValidFrom).Select(item => item.ValidFrom).ToListAsync());
        Assert.Equal(2, await check.MerchandiserAreas.CountAsync());
    }

    [Fact]
    public async Task A_retainer_needs_an_amount()
    {
        await using var db = CreateDbContext();

        var result = await Service(db).ImportAsync(Document() with { Payment = "retainer", RetainerAmount = null }, "user:test", CancellationToken.None);

        Assert.False(result.Saved);
        Assert.Contains("A retainer needs retainerAmount.", result.Problems);
    }

    private static MerchandiserSettingsService Service(PvmDbContext db) => new(db, new FixedClock());

    private PvmDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<PvmDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
