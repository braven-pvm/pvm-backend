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

public sealed class SellOutQueriesTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Imports_issues_and_unmapped_products_are_listed()
    {
        var month = (string units) => MakroDispoParserTests.Row("EA", "1.000", ["0", "0", "0", units, "6", "0", "0", "0"]);
        var good = await ImportAsync("a.xls", month("2"), MakroDispoParserTests.Row("EA", "1.000", ["0", "0", "0", "0", "3", "0", "0", "0"], site: "A02"));
        var refused = await ImportAsync("b.txt", "not a report");

        await using var db = CreateDbContext();
        var queries = new SellOutQueries(db);

        var imports = await queries.ListImportsAsync(10, CancellationToken.None);
        Assert.Equal([refused, good], imports.Select(import => import.Id));
        Assert.Equal(SellOutImportStatuses.Refused, imports[0].Status);
        Assert.Equal(3, imports[1].RowsLoaded);

        var unmapped = Assert.Single(await queries.ListUnmappedAsync(CancellationToken.None));
        Assert.Equal((MakroDispoParser.SourceName, "100000001", "6000000000001"), (unmapped.Source, unmapped.ProductCode, unmapped.Gtin));
        Assert.Equal(11m, unmapped.Units);
        Assert.Equal(2, unmapped.Stores);
        Assert.Equal(new DateOnly(2026, 10, 1), unmapped.LastMonth);
        Assert.Empty(await queries.ListIssuesAsync(good, CancellationToken.None));
    }

    [Fact]
    public async Task Products_are_found_by_id_or_description()
    {
        await using (var seed = CreateDbContext())
        {
            seed.ReportingProducts.AddRange(
                new ReportingProductEntity { InventoryId = "BAR45CN", Description = "Energy bar 45g choc nut", UpdatedAt = DateTimeOffset.UtcNow },
                new ReportingProductEntity { InventoryId = "GEL01", Description = "Energy gel lemon", UpdatedAt = DateTimeOffset.UtcNow });
            await seed.SaveChangesAsync();
        }

        await using var db = CreateDbContext();
        var queries = new SellOutQueries(db);

        Assert.Equal(["BAR45CN"], (await queries.SearchProductsAsync("choc", 10, CancellationToken.None)).Select(product => product.InventoryId));
        Assert.Equal(["GEL01"], (await queries.SearchProductsAsync("gel", 10, CancellationToken.None)).Select(product => product.InventoryId));
        Assert.Equal(2, (await queries.SearchProductsAsync(null, 10, CancellationToken.None)).Count);
    }

    private async Task<Guid> ImportAsync(string name, params string[] rows)
    {
        var content = name.EndsWith(".xls", StringComparison.Ordinal)
            ? Encoding.Latin1.GetBytes(MakroDispoParserTests.File(rows))
            : Encoding.UTF8.GetBytes(string.Join('\n', rows));
        await using var db = CreateDbContext();
        var service = new SellOutImportService(db, [new MakroDispoParser()], new ConfigurationBuilder().Build(), TimeProvider.System, NullLogger<SellOutImportService>.Instance);
        return (await service.ImportAsync(new SellOutFile(name, content, new DateOnly(2026, 11, 6)), "upload:test", CancellationToken.None)).ImportId;
    }

    private PvmDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<PvmDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);
}
