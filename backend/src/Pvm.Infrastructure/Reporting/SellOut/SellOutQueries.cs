using Microsoft.EntityFrameworkCore;
using Pvm.Infrastructure.Persistence;

namespace Pvm.Infrastructure.Reporting.SellOut;

public sealed record SellOutImportSummary(
    Guid Id,
    string Source,
    string FileName,
    DateOnly FileDate,
    string ReceivedBy,
    string Status,
    string? StatusReason,
    DateOnly? FirstMonth,
    DateOnly? LastMonth,
    int RowsRead,
    int RowsLoaded,
    int RowsUnmapped,
    int RowsRefused,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LoadedAt);

public sealed record SellOutIssue(int RowNumber, string Reason, string? RawKey);

/// <summary>A product code of a source that maps to no Acumatica item yet.</summary>
/// <param name="Stores">The stores with sales of the product.</param>
/// <param name="LastMonth">The first day of the last month with sales.</param>
public sealed record UnmappedSellOutProduct(
    string Source,
    string ProductCode,
    string? Gtin,
    int Rows,
    decimal Units,
    int Stores,
    DateOnly LastMonth);

public sealed record SellOutProductChoice(string InventoryId, string Description, string? ItemClassName);

/// <summary>The reads that the workbench pages of the sell-out import need.</summary>
public sealed class SellOutQueries(PvmDbContext db)
{
    public async Task<IReadOnlyList<SellOutImportSummary>> ListImportsAsync(int take, CancellationToken cancellationToken) =>
        await db.SellOutImports
            .AsNoTracking()
            .OrderByDescending(import => import.CreatedAt)
            .Take(Math.Clamp(take, 1, 200))
            .Select(import => new SellOutImportSummary(
                import.Id, import.Source, import.FileName, import.FileDate, import.ReceivedBy, import.Status, import.StatusReason,
                import.FirstMonth, import.LastMonth, import.RowsRead, import.RowsLoaded, import.RowsUnmapped, import.RowsRefused,
                import.CreatedAt, import.LoadedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SellOutIssue>> ListIssuesAsync(Guid importId, CancellationToken cancellationToken) =>
        await db.SellOutImportIssues
            .AsNoTracking()
            .Where(issue => issue.ImportId == importId)
            .OrderBy(issue => issue.RowNumber)
            .Take(1000)
            .Select(issue => new SellOutIssue(issue.RowNumber, issue.Reason, issue.RawKey))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<UnmappedSellOutProduct>> ListUnmappedAsync(CancellationToken cancellationToken)
    {
        var rows = await db.SellOutMonths
            .AsNoTracking()
            .Where(month => month.InventoryId == null)
            .GroupBy(month => new { month.Source, month.SourceProductCode })
            .Select(group => new
            {
                group.Key.Source,
                group.Key.SourceProductCode,
                Gtin = group.Max(month => month.Gtin),
                Rows = group.Count(),
                Units = group.Sum(month => month.UnitsEach),
                Stores = group.Select(month => month.StoreId).Distinct().Count(),
                LastPeriod = group.Max(month => (month.Year * 100) + month.Month),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new UnmappedSellOutProduct(
                row.Source, row.SourceProductCode, row.Gtin, row.Rows, row.Units, row.Stores,
                new DateOnly(row.LastPeriod / 100, row.LastPeriod % 100, 1)))
            .OrderByDescending(row => row.Units)
            .ToList();
    }

    /// <summary>Acumatica items to map to, from the reporting products that the sell-in load keeps. PVM has a few hundred items, so the page lists them all.</summary>
    public async Task<IReadOnlyList<SellOutProductChoice>> SearchProductsAsync(string? text, int take, CancellationToken cancellationToken)
    {
        var query = db.ReportingProducts.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(text))
        {
            var pattern = $"%{text.Trim().Replace("%", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal)}%";
            query = query.Where(product => EF.Functions.ILike(product.InventoryId, pattern) || EF.Functions.ILike(product.Description, pattern));
        }

        return await query
            .OrderBy(product => product.InventoryId)
            .Take(Math.Clamp(take, 1, 1000))
            .Select(product => new SellOutProductChoice(product.InventoryId, product.Description, product.ItemClassName))
            .ToListAsync(cancellationToken);
    }
}
