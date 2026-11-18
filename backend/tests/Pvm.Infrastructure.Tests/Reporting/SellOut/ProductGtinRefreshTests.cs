using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Pvm.Application.Reporting.SellOut;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Pvm.Infrastructure.Reporting.SellOut;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Reporting.SellOut;

public sealed class ProductGtinRefreshTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();
    private readonly FakeGtins _source = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    // Made-up values in the shape of the contract API entity StockItem with $expand=CrossReferences.
    private static string Item(string inventoryId, params (string Id, string Type, string? Uom)[] references) => $$"""
        {
          "id": "{{Guid.NewGuid()}}", "InventoryID": { "value": "{{inventoryId}}  " },
          "CrossReferences": [ {{string.Join(",", references.Select(reference =>
              $$"""{ "AlternateID": { "value": "{{reference.Id}}" }, "AlternateType": { "value": "{{reference.Type}}" }, "UOM": { "value": {{(reference.Uom is null ? "null" : $"\"{reference.Uom}\"")}} } }"""))}} ]
        }
        """;

    [Fact]
    public void The_parser_keeps_valid_gtins_and_drops_other_cross_references()
    {
        var page = $"[ {Item("ITEM01", ("6000000000001", "Barcode", "EA"), ("ABC-123", "Customer Part Number", null), ("16000000000008", "Barcode", "CS"))}, {Item("ITEM02")} ]";

        var gtins = ProductGtinParser.ParsePage(page);

        Assert.Equal(["6000000000001", "16000000000008"], gtins.Select(gtin => gtin.Gtin));
        Assert.All(gtins, gtin => Assert.Equal("ITEM01", gtin.InventoryId));
        Assert.Equal("EA", gtins[0].Uom);
        Assert.Equal(2, ProductGtinParser.CountItems(page));
    }

    [Fact]
    public async Task A_refresh_keeps_the_current_gtins_drops_old_ones_and_skips_a_gtin_on_two_items()
    {
        await using (var seed = CreateDbContext())
        {
            seed.ProductGtins.Add(new ProductGtinEntity { Gtin = "6000000000099", InventoryId = "OLD", UpdatedAt = DateTimeOffset.UtcNow });
            await seed.SaveChangesAsync();
        }

        _source.Set(
            new ProductGtin("6000000000001", "ITEM01", "EA"),
            new ProductGtin("6000000000002", "ITEM02", "EA"),
            new ProductGtin("6000000000002", "ITEM03", "EA"),
            new ProductGtin("6000000000001", "ITEM01", "SW"));

        var result = await RefreshAsync();

        Assert.Equal(1, result.Gtins);
        Assert.Equal(["6000000000002"], result.Ambiguous);
        await using var db = CreateDbContext();
        var stored = await db.ProductGtins.SingleAsync();
        Assert.Equal(("6000000000001", "ITEM01"), (stored.Gtin, stored.InventoryId));
    }

    [Fact]
    public async Task A_refresh_maps_sell_out_rows_that_waited_for_their_gtin()
    {
        var file = new SellOutFile(
            "Makro Dispo Report PVM 05112026.xls",
            Encoding.Latin1.GetBytes(MakroDispoParserTests.File(
                MakroDispoParserTests.Row("EA", "1.000", ["0", "0", "0", "0", "6", "0", "0", "0"], barcode: "6000000000001"))),
            new DateOnly(2026, 11, 6));
        await using (var db = CreateDbContext())
        {
            var import = new SellOutImportService(db, [new MakroDispoParser()], new ConfigurationBuilder().Build(), TimeProvider.System, NullLogger<SellOutImportService>.Instance);
            Assert.Equal(1, (await import.ImportAsync(file, "upload:test", CancellationToken.None)).RowsUnmapped);
        }

        _source.Set(new ProductGtin("6000000000001", "ITEM01", "EA"));
        var result = await RefreshAsync();

        Assert.Equal(1, result.ProductsMapped);
        await using var check = CreateDbContext();
        Assert.Equal("ITEM01", (await check.SellOutMonths.SingleAsync()).InventoryId);
        Assert.Equal("ITEM01", (await check.StoreStockSnapshots.SingleAsync()).InventoryId);
        var map = await check.SellOutProductMaps.SingleAsync();
        Assert.Equal((MakroDispoParser.SourceName, "100000001", SellOutMapMethods.Gtin), (map.Source, map.SourceProductCode, map.Method));
    }

    [Fact]
    public async Task An_empty_read_changes_nothing()
    {
        await using (var seed = CreateDbContext())
        {
            seed.ProductGtins.Add(new ProductGtinEntity { Gtin = "6000000000001", InventoryId = "ITEM01", UpdatedAt = DateTimeOffset.UtcNow });
            await seed.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => RefreshAsync());

        await using var db = CreateDbContext();
        Assert.Equal(1, await db.ProductGtins.CountAsync());
    }

    private async Task<ProductGtinRefreshResult> RefreshAsync()
    {
        await using var db = CreateDbContext();
        return await new ProductGtinRefreshService(db, _source, TimeProvider.System, NullLogger<ProductGtinRefreshService>.Instance)
            .RefreshAsync(CancellationToken.None);
    }

    private PvmDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<PvmDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private sealed class FakeGtins : IProductGtinSource
    {
        private IReadOnlyList<ProductGtin> _gtins = [];

        public void Set(params ProductGtin[] gtins) => _gtins = gtins;

        public Task<IReadOnlyList<ProductGtin>> ReadAllAsync(CancellationToken cancellationToken) => Task.FromResult(_gtins);
    }
}
