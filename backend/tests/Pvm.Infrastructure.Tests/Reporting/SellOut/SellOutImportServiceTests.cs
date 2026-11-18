using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Pvm.Application.Messaging;
using Pvm.Application.Reporting.SellOut;
using Pvm.Infrastructure.PayloadArchive;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Pvm.Infrastructure.Reporting.SellOut;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Reporting.SellOut;

public sealed class SellOutImportServiceTests : IAsyncLifetime
{
    private static readonly string[] NoSales = ["0", "0", "0", "0", "0", "0", "0", "0"];

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();
    private readonly string _archiveRoot = Path.Combine(Path.GetTempPath(), $"pvm-sell-out-{Guid.NewGuid():N}");

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        if (Directory.Exists(_archiveRoot))
        {
            Directory.Delete(_archiveRoot, recursive: true);
        }
    }

    private static SellOutFile Makro(params string[] rows) =>
        new("Makro Dispo Report PVM 05112026.xls", Encoding.Latin1.GetBytes(MakroDispoParserTests.File(rows)), new DateOnly(2026, 11, 6));

    private static string Sold(int october, string site = "M01", string article = "100000001", string barcode = "6000000000001", string soh = "0.000") =>
        MakroDispoParserTests.Row("EA", "1.000", ["0", "0", "0", "0", october.ToString(System.Globalization.CultureInfo.InvariantCulture), "0", "0", "0"],
            site: site, article: article, barcode: barcode, soh: soh);

    [Fact]
    public async Task A_file_loads_its_months_stock_and_stores_and_maps_products_by_gtin()
    {
        await using (var seed = CreateDbContext())
        {
            seed.ProductGtins.Add(new ProductGtinEntity { Gtin = "6000000000001", InventoryId = "ITEM01", UpdatedAt = DateTimeOffset.UtcNow });
            await seed.SaveChangesAsync();
        }

        var result = await ImportAsync(Makro(Sold(6, soh: "4.000"), Sold(3, site: "A02", article: "100000002", barcode: "6000000000009")));

        Assert.Equal(SellOutImportStatuses.Loaded, result.Status);
        Assert.Equal(MakroDispoParser.SourceName, result.Source);
        Assert.Equal(2, result.RowsLoaded);
        Assert.Equal(1, result.RowsUnmapped);
        Assert.Equal(2, result.NewStores);
        Assert.Equal(["100000002"], result.UnmappedProducts);

        await using var db = CreateDbContext();
        var months = await db.SellOutMonths.OrderBy(month => month.SourceProductCode).ToListAsync();
        Assert.Equal(["ITEM01", null], months.Select(month => month.InventoryId));
        Assert.All(months, month =>
        {
            Assert.Equal(2027, month.FinancialYear);
            Assert.Equal(8, month.FinancialMonth);
            Assert.Equal(result.ImportId, month.ImportId);
        });
        Assert.Equal(4m, (await db.StoreStockSnapshots.SingleAsync(stock => stock.SourceProductCode == "100000001")).StockOnHand);
        Assert.Equal(["A02", "M01"], await db.RetailStores.OrderBy(store => store.StoreCode).Select(store => store.StoreCode).ToListAsync());
        var map = await db.SellOutProductMaps.SingleAsync();
        Assert.Equal(("100000001", "ITEM01", SellOutMapMethods.Gtin), (map.SourceProductCode, map.InventoryId, map.Method));

        var import = await db.SellOutImports.SingleAsync();
        Assert.Equal(SellOutImportStatuses.Loaded, import.Status);
        Assert.Equal(new DateOnly(2026, 6, 1), import.FirstMonth);
        Assert.Equal(new DateOnly(2026, 11, 1), import.LastMonth);
        Assert.NotNull(import.ArchivePath);
        Assert.Single(Directory.GetFiles(_archiveRoot, "*", SearchOption.AllDirectories));

        var run = await db.IntegrationRuns.SingleAsync();
        Assert.Equal(SellOutImportService.RunType, run.RunType);
        Assert.Equal(IntegrationRunStatuses.Succeeded, run.Status);
    }

    [Fact]
    public async Task A_later_file_replaces_the_months_of_its_source_and_keeps_other_months()
    {
        await ImportAsync(Makro(Sold(6), Sold(3, site: "A02")));
        // The second file has a new run date, so it covers July to December. A02 sold nothing in October.
        // In this file, October is the 4th month column.
        var octoberNine = MakroDispoParserTests.Row("EA", "1.000", ["0", "0", "0", "9", "0", "0", "0", "0"]);
        var later = MakroDispoParserTests.File(octoberNine).Replace("05.11.2026", "05.12.2026", StringComparison.Ordinal)
            .Replace("06-2026\t07-2026\t08-2026\t09-2026\t10-2026\t11-2026\t11-2025", "07-2026\t08-2026\t09-2026\t10-2026\t11-2026\t12-2026\t12-2025", StringComparison.Ordinal);
        var laterFile = new SellOutFile("Makro Dispo Report PVM 05122026.xls", Encoding.Latin1.GetBytes(later), new DateOnly(2026, 12, 6));

        var result = await ImportAsync(laterFile);

        Assert.Equal(SellOutImportStatuses.Loaded, result.Status);
        await using var db = CreateDbContext();
        var october = await db.SellOutMonths.Where(month => month.Month == 10).ToListAsync();
        Assert.Equal(9m, Assert.Single(october).UnitsEach);
        // Stock is a snapshot for each file date, so the stock of both files stays.
        Assert.Equal(3, await db.StoreStockSnapshots.CountAsync());
    }

    [Fact]
    public async Task The_same_file_twice_is_refused_and_changes_nothing()
    {
        var file = Makro(Sold(6));
        var first = await ImportAsync(file);

        var second = await ImportAsync(file);

        Assert.Equal(SellOutImportStatuses.Refused, second.Status);
        Assert.Contains(first.ImportId.ToString(), second.Reason, StringComparison.Ordinal);
        await using var db = CreateDbContext();
        Assert.Equal(1, await db.SellOutMonths.CountAsync());
        Assert.Equal(2, await db.SellOutImports.CountAsync());
    }

    [Fact]
    public async Task An_unknown_file_is_refused()
    {
        var result = await ImportAsync(new SellOutFile("notes.txt", "Some text"u8.ToArray(), new DateOnly(2026, 11, 6)));

        Assert.Equal(SellOutImportStatuses.Refused, result.Status);
        Assert.Equal("unknown-format", result.Reason);
        await using var db = CreateDbContext();
        Assert.Equal(SellOutImportStatuses.Refused, (await db.SellOutImports.SingleAsync()).Status);
        Assert.Empty(await db.SellOutMonths.ToListAsync());
    }

    [Fact]
    public async Task Too_many_bad_rows_refuse_the_file_and_keep_the_issues()
    {
        var bad = MakroDispoParserTests.Row("EA", "1.000", ["0", "0", "0", "0", "x", "0", "0", "0"], site: "A02");

        var result = await ImportAsync(Makro(Sold(6), bad));

        Assert.Equal(SellOutImportStatuses.Refused, result.Status);
        Assert.Equal("too-many-bad-rows", result.Reason);
        await using var db = CreateDbContext();
        Assert.Empty(await db.SellOutMonths.ToListAsync());
        Assert.Equal(6, (await db.SellOutImportIssues.SingleAsync()).RowNumber);
    }

    [Fact]
    public async Task A_preview_reads_and_maps_but_writes_nothing()
    {
        var preview = await CreateService(CreateDbContext()).PreviewAsync(Makro(Sold(6)), CancellationToken.None);

        Assert.Equal(SellOutImportStatuses.Previewed, preview.Status);
        Assert.Equal(1, preview.RowsLoaded);
        Assert.Equal(1, preview.NewStores);
        Assert.Equal(["100000001"], preview.UnmappedProducts);
        await using var db = CreateDbContext();
        Assert.Empty(await db.SellOutImports.ToListAsync());
        Assert.Empty(await db.SellOutMonths.ToListAsync());
        Assert.Empty(await db.RetailStores.ToListAsync());
    }

    [Fact]
    public async Task Mapping_a_product_later_fills_the_item_on_its_rows()
    {
        await ImportAsync(Makro(Sold(6), Sold(2, site: "A02")));

        await using (var db = CreateDbContext())
        {
            var updated = await CreateService(db).MapProductAsync(MakroDispoParser.SourceName, "100000001", "ITEM07", 1m, "user:test", CancellationToken.None);
            Assert.Equal(4, updated); // 2 month rows and 2 stock rows.
        }

        await using var check = CreateDbContext();
        Assert.All(await check.SellOutMonths.ToListAsync(), month => Assert.Equal("ITEM07", month.InventoryId));
        Assert.Equal(SellOutMapMethods.Manual, (await check.SellOutProductMaps.SingleAsync()).Method);
    }

    [Fact]
    public async Task Shoprite_articles_map_through_the_shoprite_item_mapping()
    {
        await using (var seed = CreateDbContext())
        {
            seed.ShopriteItemMappings.Add(new ShopriteItemMappingEntity
            {
                Id = Guid.NewGuid(),
                AcumaticaInventoryId = "ITEM03",
                ShopriteBuyerItemId = "10000001",
                Gtin = "6000000000003",
                IsVerified = true,
                UpdatedBy = "test",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        var file = new SellOutFile("vnd-art-sas.xlsx", ShopriteVasParserTests.File(ShopriteVasParserTests.Row()), new DateOnly(2026, 11, 12));
        var result = await ImportAsync(file);

        Assert.Equal(SellOutImportStatuses.Loaded, result.Status);
        await using var db = CreateDbContext();
        Assert.All(await db.SellOutMonths.ToListAsync(), month => Assert.Equal("ITEM03", month.InventoryId));
        Assert.Equal(SellOutMapMethods.ShopriteItemMapping, (await db.SellOutProductMaps.SingleAsync()).Method);
    }

    private async Task<SellOutImportResult> ImportAsync(SellOutFile file)
    {
        await using var db = CreateDbContext();
        return await CreateService(db).ImportAsync(file, "upload:test", CancellationToken.None);
    }

    private SellOutImportService CreateService(PvmDbContext db) => new(
        db,
        [new MakroDispoParser(), new ShopriteVasParser(), .. WholesalerListParser.All()],
        new ConfigurationBuilder().Build(),
        TimeProvider.System,
        NullLogger<SellOutImportService>.Instance,
        new FileSystemPayloadArchive(_archiveRoot, "payloads"));

    private PvmDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<PvmDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);
}
