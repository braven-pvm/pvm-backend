using Microsoft.EntityFrameworkCore;
using Pvm.Application.Stock;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Pvm.Infrastructure.Stock;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Stock;

public sealed class StockSyncRunRecorderTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Latest_run_returns_null_when_nothing_is_recorded()
    {
        await using var db = await CreateDbContextAsync();

        Assert.Null(await Recorder(db).GetLatestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Shadow_run_is_recorded_with_counts_and_every_line()
    {
        await using var db = await CreateDbContextAsync();
        var result = new StockSyncResult(
            StockSyncMode.Shadow,
            [
                Line("KLER6", StockSyncAction.Update, current: 35, next: 9, level: 9.5m),
                Line("DYNA22", StockSyncAction.Unchanged, current: 16, next: 16, level: 16m),
                new StockSyncLine(
                    new WebStockItem("ENER1", 10, 11, WebStockManagement.Parent, null),
                    StockSyncAction.Skip,
                    null,
                    null,
                    0m,
                    "this variation takes its stock from the parent product"),
            ],
            Written: 0,
            Refusal: null);

        await Recorder(db).RecordAsync(result, Now.AddMinutes(-2), CancellationToken.None);
        var run = await Recorder(db).GetLatestAsync(CancellationToken.None);

        Assert.NotNull(run);
        Assert.Equal("Shadow", run.Mode);
        Assert.Equal("Succeeded", run.Status);
        Assert.Equal(Now.AddMinutes(-2), run.StartedAt);
        Assert.Equal(Now, run.CompletedAt);
        Assert.Equal(3, run.ItemCount);
        Assert.Equal(1, run.UpdateCount);
        Assert.Equal(1, run.UnchangedCount);
        Assert.Equal(1, run.SkippedCount);
        Assert.Equal(0, run.WrittenCount);
        var kler = Assert.Single(run.Lines, line => line.Sku == "KLER6");
        Assert.Equal("Update", kler.Action);
        Assert.Equal(35, kler.CurrentQuantity);
        Assert.Equal(9, kler.NewQuantity);
        Assert.Equal(9.5m, kler.AcumaticaLevel);
        var ener = Assert.Single(run.Lines, line => line.Sku == "ENER1");
        Assert.Equal(11, ener.VariationId);
        Assert.Equal("Skip", ener.Action);
        Assert.Null(ener.NewQuantity);
    }

    [Fact]
    public async Task Figures_are_stored_with_each_line_and_read_back_by_name()
    {
        await using var db = await CreateDbContextAsync();
        var figures = new AcumaticaStockFigures("CER07", 3307, 3000, 307, 10, 0, 5);
        var result = new StockSyncResult(
            StockSyncMode.Shadow,
            [new StockSyncLine(new WebStockItem("CER07", 100, null, WebStockManagement.Own, 3297), StockSyncAction.Update, 0, -15m, 0m, null, figures)],
            Written: 0,
            Refusal: null);

        await Recorder(db).RecordAsync(result, Now, CancellationToken.None);
        var run = await Recorder(db).GetLatestAsync(CancellationToken.None);
        var json = await db.StockSyncRuns.Select(entity => entity.LinesJson).SingleAsync();

        var stored = Assert.Single(run!.Lines).Figures;
        Assert.Equal(new StockFiguresView(3307, 3000, 307, 10, 0, 5, null), stored);
        // The names the workbench client reads.
        foreach (var name in new[] { "onHand", "heldBackAdjustments", "autoAdjustments", "openOrders", "promoOrders", "transfersOut", "warehouseCommitted" })
        {
            Assert.Contains($"\"{name}\"", json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_run_recorded_before_figures_existed_reads_back_with_none()
    {
        await using var db = await CreateDbContextAsync();
        db.StockSyncRuns.Add(new StockSyncRunEntity
        {
            Id = Guid.NewGuid(),
            Mode = "Shadow",
            Status = "Succeeded",
            StartedAt = Now,
            CompletedAt = Now,
            LinesJson = """[{"sku":"KLER6","productId":1,"variationId":null,"action":"Update","currentQuantity":35,"newQuantity":9,"acumaticaLevel":9,"webPending":0,"reason":null}]""",
        });
        await db.SaveChangesAsync();

        var run = await Recorder(db).GetLatestAsync(CancellationToken.None);

        Assert.Null(Assert.Single(run!.Lines).Figures);
    }

    [Fact]
    public async Task Refused_write_run_keeps_the_reason()
    {
        await using var db = await CreateDbContextAsync();
        var result = new StockSyncResult(
            StockSyncMode.Write,
            [Line("KLER5", StockSyncAction.Update, current: 14, next: 0, level: 1m)],
            Written: 0,
            Refusal: "11 items would go to 0; the limit is 10");

        await Recorder(db).RecordAsync(result, Now, CancellationToken.None);
        var run = await Recorder(db).GetLatestAsync(CancellationToken.None);

        Assert.NotNull(run);
        Assert.Equal("Refused", run.Status);
        Assert.Equal("11 items would go to 0; the limit is 10", run.Detail);
    }

    [Fact]
    public async Task Write_that_fails_partway_keeps_its_lines_and_the_writes_made()
    {
        await using var db = await CreateDbContextAsync();
        var result = new StockSyncResult(
            StockSyncMode.Write,
            [
                Line("DYNA24", StockSyncAction.Update, current: 1, next: 20, level: 20m),
                Line("ENER12", StockSyncAction.Update, current: 1, next: 30, level: 30m),
            ],
            Written: 1,
            Refusal: null,
            Failure: new string('x', 2500));

        await Recorder(db).RecordAsync(result, Now, CancellationToken.None);
        var run = await Recorder(db).GetLatestAsync(CancellationToken.None);

        Assert.NotNull(run);
        Assert.Equal("Failed", run.Status);
        Assert.Equal(1, run.WrittenCount);
        Assert.Equal(2, run.UpdateCount);
        Assert.Equal(2, run.Lines.Count);
        Assert.Equal(2000, run.Detail!.Length);
    }

    [Fact]
    public async Task Failed_run_is_recorded_and_is_the_latest()
    {
        await using var db = await CreateDbContextAsync();
        var shadow = new StockSyncResult(StockSyncMode.Shadow, [], 0, null);
        await Recorder(db, Now.AddMinutes(-30)).RecordAsync(shadow, Now.AddMinutes(-31), CancellationToken.None);

        await Recorder(db).RecordFailureAsync(
            StockSyncMode.Shadow, Now.AddMinutes(-1), new HttpRequestException("Acumatica sign-in failed"), CancellationToken.None);
        var run = await Recorder(db).GetLatestAsync(CancellationToken.None);

        Assert.NotNull(run);
        Assert.Equal("Failed", run.Status);
        Assert.Equal("Acumatica sign-in failed", run.Detail);
        Assert.Empty(run.Lines);
    }

    [Fact]
    public async Task Runs_older_than_thirty_days_are_deleted()
    {
        await using var db = await CreateDbContextAsync();
        var empty = new StockSyncResult(StockSyncMode.Shadow, [], 0, null);
        await Recorder(db, Now.AddDays(-31)).RecordAsync(empty, Now.AddDays(-31), CancellationToken.None);
        await Recorder(db, Now.AddDays(-29)).RecordAsync(empty, Now.AddDays(-29), CancellationToken.None);

        await Recorder(db).RecordAsync(empty, Now, CancellationToken.None);

        Assert.Equal(2, await db.StockSyncRuns.CountAsync());
    }

    private static StockSyncLine Line(string sku, StockSyncAction action, int current, int next, decimal level)
        => new(
            new WebStockItem(sku, 100, null, WebStockManagement.Own, current),
            action,
            next,
            level,
            0m,
            null);

    private static StockSyncRunRecorder Recorder(PvmDbContext db, DateTimeOffset? now = null)
        => new(db, new FixedTimeProvider(now ?? Now));

    private async Task<PvmDbContext> CreateDbContextAsync()
    {
        var options = new DbContextOptionsBuilder<PvmDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        var db = new PvmDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
