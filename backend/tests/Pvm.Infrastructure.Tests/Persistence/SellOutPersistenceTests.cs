using Microsoft.EntityFrameworkCore;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Persistence;

public sealed class SellOutPersistenceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 8, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task An_import_with_its_months_stock_and_issues_round_trips_in_the_reporting_schema()
    {
        var importId = Guid.NewGuid();
        await using (var db = CreateDbContext())
        {
            var store = NewStore("makro", "1001");
            db.RetailStores.Add(store);
            db.SellOutImports.Add(NewImport(importId, "hash-1", SellOutImportStatuses.Loaded));
            db.SellOutImportIssues.Add(new SellOutImportIssueEntity
            {
                Id = Guid.NewGuid(),
                ImportId = importId,
                RowNumber = 12,
                Reason = "unreadable-units",
                RawKey = "1001 / 100000001",
            });
            db.SellOutMonths.Add(NewMonth(importId, store.Id, "100000001", 2026, 10, units: 24m));
            db.StoreStockSnapshots.Add(new StoreStockSnapshotEntity
            {
                Id = Guid.NewGuid(),
                ImportId = importId,
                Source = "makro-dispo",
                StoreId = store.Id,
                SourceProductCode = "100000001",
                SnapshotDate = new DateOnly(2026, 11, 5),
                StockOnHand = 36m,
                StockOnOrder = 12m,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext())
        {
            var import = await db.SellOutImports.SingleAsync();
            Assert.Equal(SellOutImportStatuses.Loaded, import.Status);
            Assert.Equal(new DateOnly(2026, 6, 1), import.FirstMonth);
            var month = await db.SellOutMonths.SingleAsync();
            Assert.Equal(24m, month.Units);
            Assert.Equal(2027, month.FinancialYear);
            Assert.Equal(8, month.FinancialMonth);
            Assert.Null(month.InventoryId);
            Assert.Equal(36m, (await db.StoreStockSnapshots.SingleAsync()).StockOnHand);
            Assert.Equal(12, (await db.SellOutImportIssues.SingleAsync()).RowNumber);

            var schemas = await db.Database
                .SqlQueryRaw<string>("""
                    SELECT DISTINCT table_schema AS "Value" FROM information_schema.tables
                    WHERE table_name IN ('sell_out_imports', 'sell_out_import_issues', 'sell_out_months',
                        'store_stock_snapshots', 'retail_stores', 'sell_out_product_maps', 'product_gtins')
                    """)
                .ToListAsync();
            Assert.Equal(["reporting"], schemas);
        }
    }

    [Fact]
    public async Task A_store_code_is_unique_for_each_retailer()
    {
        await using var db = CreateDbContext();
        db.RetailStores.Add(NewStore("makro", "1001"));
        db.RetailStores.Add(NewStore("shoprite-group", "1001"));
        await db.SaveChangesAsync();

        db.RetailStores.Add(NewStore("makro", "1001"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_source_month_holds_one_row_for_each_store_and_product()
    {
        var importId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var store = NewStore("makro", "1001");
        db.RetailStores.Add(store);
        db.SellOutImports.Add(NewImport(importId, "hash-1", SellOutImportStatuses.Loaded));
        db.SellOutMonths.Add(NewMonth(importId, store.Id, "100000001", 2026, 10, units: 24m));
        db.SellOutMonths.Add(NewMonth(importId, store.Id, "100000001", 2026, 9, units: 12m));
        await db.SaveChangesAsync();

        db.SellOutMonths.Add(NewMonth(importId, store.Id, "100000001", 2026, 10, units: 1m));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task The_same_file_can_be_loaded_only_once_but_previewed_again()
    {
        await using var db = CreateDbContext();
        db.SellOutImports.Add(NewImport(Guid.NewGuid(), "hash-1", SellOutImportStatuses.Loaded));
        db.SellOutImports.Add(NewImport(Guid.NewGuid(), "hash-1", SellOutImportStatuses.Previewed));
        db.SellOutImports.Add(NewImport(Guid.NewGuid(), "hash-1", SellOutImportStatuses.Refused));
        await db.SaveChangesAsync();

        db.SellOutImports.Add(NewImport(Guid.NewGuid(), "hash-1", SellOutImportStatuses.Loaded));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_product_code_maps_once_for_each_source_and_a_gtin_maps_to_one_item()
    {
        await using (var db = CreateDbContext())
        {
            db.SellOutProductMaps.Add(NewMap("wholesaler-cj", "PVM45CN"));
            db.SellOutProductMaps.Add(NewMap("wholesaler-topmed", "PVM45CN"));
            db.ProductGtins.Add(new ProductGtinEntity { Gtin = "6000000000001", InventoryId = "ITEM01", UpdatedAt = Now });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext())
        {
            db.SellOutProductMaps.Add(NewMap("wholesaler-cj", "PVM45CN"));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using (var db = CreateDbContext())
        {
            db.ProductGtins.Add(new ProductGtinEntity { Gtin = "6000000000001", InventoryId = "ITEM02", UpdatedAt = Now });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    private static RetailStoreEntity NewStore(string retailer, string code) => new()
    {
        Id = Guid.NewGuid(),
        Retailer = retailer,
        StoreCode = code,
        Name = $"Example store {code}",
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    private static SellOutImportEntity NewImport(Guid id, string hash, string status) => new()
    {
        Id = id,
        Source = "makro-dispo",
        FileName = "example.xls",
        FileSha256 = hash,
        FileSize = 1024,
        FileDate = new DateOnly(2026, 11, 5),
        ReceivedBy = "upload:test",
        Status = status,
        FirstMonth = new DateOnly(2026, 6, 1),
        LastMonth = new DateOnly(2026, 11, 1),
        CreatedAt = Now,
    };

    private static SellOutMonthEntity NewMonth(Guid importId, Guid storeId, string code, int year, int month, decimal units) => new()
    {
        Id = Guid.NewGuid(),
        ImportId = importId,
        Source = "makro-dispo",
        StoreId = storeId,
        SourceProductCode = code,
        Year = year,
        Month = month,
        FinancialYear = month >= 3 ? year + 1 : year,
        FinancialMonth = (month + 9) % 12 + 1,
        Units = units,
        UnitsEach = units,
    };

    private static SellOutProductMapEntity NewMap(string source, string code) => new()
    {
        Source = source,
        SourceProductCode = code,
        InventoryId = "ITEM01",
        UnitsPerEach = 1m,
        Method = SellOutMapMethods.Manual,
        MappedBy = "test",
        MappedAt = Now,
    };

    private PvmDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<PvmDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);
}
