using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pvm.Application.Stock;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Stock;

/// <summary>One product's decision in a recorded run, as the workbench shows it.</summary>
public sealed record StockSyncLineView(
    string Sku,
    long ProductId,
    long? VariationId,
    string Action,
    int? CurrentQuantity,
    int? NewQuantity,
    decimal? AcumaticaLevel,
    decimal WebPending,
    string? Reason);

/// <summary>A recorded stock-sync run.</summary>
public sealed record StockSyncRunView(
    Guid Id,
    string Mode,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int ItemCount,
    int UpdateCount,
    int UnchangedCount,
    int SkippedCount,
    int WrittenCount,
    string? Detail,
    IReadOnlyList<StockSyncLineView> Lines);

/// <summary>Stores each stock-sync run and reads them back for the workbench.</summary>
public sealed class StockSyncRunRecorder(PvmDbContext db, TimeProvider timeProvider)
{
    private const int KeepDays = 30;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task RecordAsync(StockSyncResult result, DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        var lines = result.Lines.Select(line => new StockSyncLineView(
            line.Item.Sku,
            line.Item.ProductId,
            line.Item.VariationId,
            line.Action.ToString(),
            line.Item.CurrentQuantity,
            line.NewQuantity,
            line.AcumaticaLevel,
            line.WebPending,
            line.Reason)).ToList();

        await AddAsync(
            result.Mode.ToString(),
            result.Refusal is null ? "Succeeded" : "Refused",
            startedAt,
            lines,
            result.Written,
            result.Refusal,
            cancellationToken);
    }

    public Task RecordFailureAsync(StockSyncMode mode, DateTimeOffset startedAt, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var detail = exception.Message.Length > 2000 ? exception.Message[..2000] : exception.Message;
        return AddAsync(mode.ToString(), "Failed", startedAt, [], 0, detail, cancellationToken);
    }

    public async Task<StockSyncRunView?> GetLatestAsync(CancellationToken cancellationToken)
    {
        var run = await db.StockSyncRuns.AsNoTracking()
            .OrderByDescending(entity => entity.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return run is null ? null : View(run);
    }

    private async Task AddAsync(
        string mode,
        string status,
        DateTimeOffset startedAt,
        IReadOnlyList<StockSyncLineView> lines,
        int written,
        string? detail,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        db.StockSyncRuns.Add(new StockSyncRunEntity
        {
            Id = Guid.NewGuid(),
            Mode = mode,
            Status = status,
            StartedAt = startedAt,
            CompletedAt = now,
            ItemCount = lines.Count,
            UpdateCount = lines.Count(line => line.Action == nameof(StockSyncAction.Update)),
            UnchangedCount = lines.Count(line => line.Action == nameof(StockSyncAction.Unchanged)),
            SkippedCount = lines.Count(line => line.Action == nameof(StockSyncAction.Skip)),
            WrittenCount = written,
            Detail = detail,
            LinesJson = JsonSerializer.Serialize(lines, Json),
        });
        await db.SaveChangesAsync(cancellationToken);

        // A run every half hour is 48 a day; a month of history is plenty to look back on.
        var cutoff = now.AddDays(-KeepDays);
        await db.StockSyncRuns.Where(entity => entity.CompletedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
    }

    private static StockSyncRunView View(StockSyncRunEntity run)
        => new(
            run.Id,
            run.Mode,
            run.Status,
            run.StartedAt,
            run.CompletedAt,
            run.ItemCount,
            run.UpdateCount,
            run.UnchangedCount,
            run.SkippedCount,
            run.WrittenCount,
            run.Detail,
            JsonSerializer.Deserialize<List<StockSyncLineView>>(run.LinesJson, Json) ?? []);
}
