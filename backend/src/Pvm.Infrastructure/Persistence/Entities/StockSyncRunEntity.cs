namespace Pvm.Infrastructure.Persistence.Entities;

/// <summary>
/// One run of the Acumatica → WooCommerce stock sync. The per-product decisions are kept as JSON,
/// so the workbench can show exactly what a run saw and decided.
/// </summary>
public sealed class StockSyncRunEntity
{
    public Guid Id { get; set; }

    /// <summary>Disabled, Shadow or Write.</summary>
    public required string Mode { get; set; }

    /// <summary>Succeeded, Refused or Failed.</summary>
    public required string Status { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset CompletedAt { get; set; }

    public int ItemCount { get; set; }

    public int UpdateCount { get; set; }

    public int UnchangedCount { get; set; }

    public int SkippedCount { get; set; }

    public int WrittenCount { get; set; }

    /// <summary>Why a write run stopped before writing, or why a run failed.</summary>
    public string? Detail { get; set; }

    public required string LinesJson { get; set; }
}
