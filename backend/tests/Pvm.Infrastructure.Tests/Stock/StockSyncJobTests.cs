using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pvm.Application.Stock;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Stock;
using Testcontainers.PostgreSql;
using Xunit;

namespace Pvm.Infrastructure.Tests.Stock;

/// <summary>The worker's stock job: one run, recorded, and a failed replica when it did not finish.</summary>
public sealed class StockSyncJobTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 8, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task A_run_that_finishes_is_recorded_as_succeeded()
    {
        await using var db = await CreateDbContextAsync();
        var store = new FakeStore([Item("DYNA24", 1)]);

        await Job(db, store, new FakeAcumatica(("DYNA24", 20m))).RunAsync(CancellationToken.None);

        var run = await new StockSyncRunRecorder(db, new FixedTime()).GetLatestAsync(CancellationToken.None);
        Assert.Equal("Succeeded", run!.Status);
        Assert.Equal(1, run.WrittenCount);
    }

    [Fact]
    public async Task A_write_that_fails_partway_is_recorded_with_its_writes_and_fails_the_job()
    {
        await using var db = await CreateDbContextAsync();
        var store = new FakeStore([Item("DYNA24", 1), Item("ENER12", 1), Item("KLER5", 1)]) { FailOn = "ENER12" };
        var job = Job(db, store, new FakeAcumatica(("DYNA24", 20m), ("ENER12", 30m), ("KLER5", 40m)));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => job.RunAsync(CancellationToken.None));

        Assert.Contains("ENER12", error.Message, StringComparison.Ordinal);
        var run = await new StockSyncRunRecorder(db, new FixedTime()).GetLatestAsync(CancellationToken.None);
        Assert.Equal("Failed", run!.Status);
        Assert.Equal(1, run.WrittenCount);
        Assert.Equal(3, run.Lines.Count);
        Assert.Equal(("DYNA24", 20), Assert.Single(store.Writes));
    }

    [Fact]
    public async Task A_read_that_fails_is_recorded_and_the_error_goes_on_to_the_job()
    {
        await using var db = await CreateDbContextAsync();
        var store = new FakeStore([Item("DYNA24", 1)]);

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => Job(db, store, new FailingAcumatica()).RunAsync(CancellationToken.None));

        Assert.Equal("Acumatica sign-in failed", error.Message);
        var run = await new StockSyncRunRecorder(db, new FixedTime()).GetLatestAsync(CancellationToken.None);
        Assert.Equal("Failed", run!.Status);
        Assert.Equal("Acumatica sign-in failed", run.Detail);
        Assert.Empty(store.Writes);
    }

    private static StockSyncJob Job(PvmDbContext db, IWooCommerceStockClient store, IAcumaticaStockReader acumatica)
    {
        var options = Options.Create(new StockSyncOptions { Mode = StockSyncMode.Write });
        return new StockSyncJob(
            new StockSyncService(store, acumatica, options, NullLogger<StockSyncService>.Instance),
            new StockSyncRunRecorder(db, new FixedTime()),
            options,
            new FixedTime(),
            NullLogger<StockSyncJob>.Instance);
    }

    private static WebStockItem Item(string sku, int current) => new(sku, 1, 2, WebStockManagement.Own, current);

    private async Task<PvmDbContext> CreateDbContextAsync()
    {
        var options = new DbContextOptionsBuilder<PvmDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        var db = new PvmDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeStore(IReadOnlyList<WebStockItem> items) : IWooCommerceStockClient
    {
        public List<(string Sku, int Quantity)> Writes { get; } = [];

        public string? FailOn { get; init; }

        public Task<IReadOnlyList<WebStockItem>> ListStockItemsAsync(CancellationToken cancellationToken)
            => Task.FromResult(items);

        public Task<IReadOnlyList<WebOrderLine>> ListPendingOrderLinesAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<WebOrderLine>>([]);

        public Task SetStockQuantityAsync(WebStockItem item, int quantity, CancellationToken cancellationToken)
        {
            if (item.Sku == FailOn)
            {
                throw new HttpRequestException("WooCommerce answered 500");
            }

            Writes.Add((item.Sku, quantity));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAcumatica(params (string Sku, decimal OnHand)[] figures) : IAcumaticaStockReader
    {
        public Task<AcumaticaStockSnapshot> ReadAsync(
            IReadOnlyCollection<string> inventoryCds,
            IReadOnlyCollection<string> webOrderIds,
            CancellationToken cancellationToken)
            => Task.FromResult(new AcumaticaStockSnapshot(
                new HashSet<string>(figures.Select(f => f.Sku), StringComparer.OrdinalIgnoreCase),
                figures.ToDictionary(
                    f => f.Sku,
                    f => new AcumaticaStockFigures(f.Sku, f.OnHand),
                    StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, string>(),
                []));
    }

    private sealed class FailingAcumatica : IAcumaticaStockReader
    {
        public Task<AcumaticaStockSnapshot> ReadAsync(
            IReadOnlyCollection<string> inventoryCds,
            IReadOnlyCollection<string> webOrderIds,
            CancellationToken cancellationToken)
            => throw new HttpRequestException("Acumatica sign-in failed");
    }
}
