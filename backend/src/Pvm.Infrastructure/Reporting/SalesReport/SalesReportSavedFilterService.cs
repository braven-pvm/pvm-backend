using Microsoft.EntityFrameworkCore;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Reporting.SalesReport;

/// <param name="Query">The query string of the report page, without the "?".</param>
/// <param name="Mine">The person who asks is the owner, and can change the filter.</param>
public sealed record SalesReportSavedFilter(Guid Id, string Name, string Query, string OwnerName, bool Shared, bool Mine, DateTimeOffset UpdatedAt);

public enum SavedFilterResult
{
    Done,
    NotFound,
    NotOwner,
    NameInUse,
    Invalid,
}

/// <summary>The named filters of the sales report. A person sees their own filters and the shared filters of others.</summary>
public sealed class SalesReportSavedFilterService(PvmDbContext db, TimeProvider time)
{
    public const int MaxName = 100;
    public const int MaxQuery = 4000;

    public async Task<IReadOnlyList<SalesReportSavedFilter>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var filters = await db.SalesReportSavedFilters.AsNoTracking()
            .Where(filter => filter.OwnerUserId == userId || filter.Shared)
            .ToListAsync(cancellationToken);
        return [.. filters
            .Select(filter => View(filter, userId))
            .OrderByDescending(filter => filter.Mine)
            .ThenBy(filter => filter.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<(SavedFilterResult Result, SalesReportSavedFilter? Filter)> CreateAsync(
        Guid userId, string ownerName, string? name, string? query, bool shared, CancellationToken cancellationToken)
    {
        name = name?.Trim();
        query = Clean(query);
        if (!Valid(name, query))
        {
            return (SavedFilterResult.Invalid, null);
        }

        if (await db.SalesReportSavedFilters.AnyAsync(filter => filter.OwnerUserId == userId && filter.Name == name, cancellationToken))
        {
            return (SavedFilterResult.NameInUse, null);
        }

        var now = time.GetUtcNow();
        var entity = new SalesReportSavedFilterEntity
        {
            Id = Guid.NewGuid(),
            Name = name!,
            Query = query!,
            OwnerUserId = userId,
            OwnerName = ownerName,
            Shared = shared,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.SalesReportSavedFilters.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return (SavedFilterResult.Done, View(entity, userId));
    }

    /// <summary>Changes the name, the query or the sharing of a filter. A null value keeps the old value.</summary>
    public async Task<(SavedFilterResult Result, SalesReportSavedFilter? Filter)> UpdateAsync(
        Guid id, Guid userId, string? name, string? query, bool? shared, CancellationToken cancellationToken)
    {
        var entity = await db.SalesReportSavedFilters.SingleOrDefaultAsync(filter => filter.Id == id, cancellationToken);
        if (entity is null || (entity.OwnerUserId != userId && !entity.Shared))
        {
            return (SavedFilterResult.NotFound, null);
        }

        if (entity.OwnerUserId != userId)
        {
            return (SavedFilterResult.NotOwner, null);
        }

        var newName = name?.Trim() ?? entity.Name;
        var newQuery = query is null ? entity.Query : Clean(query);
        if (!Valid(newName, newQuery))
        {
            return (SavedFilterResult.Invalid, null);
        }

        if (newName != entity.Name
            && await db.SalesReportSavedFilters.AnyAsync(filter => filter.OwnerUserId == userId && filter.Name == newName && filter.Id != id, cancellationToken))
        {
            return (SavedFilterResult.NameInUse, null);
        }

        entity.Name = newName;
        entity.Query = newQuery!;
        entity.Shared = shared ?? entity.Shared;
        entity.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return (SavedFilterResult.Done, View(entity, userId));
    }

    public async Task<SavedFilterResult> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var entity = await db.SalesReportSavedFilters.SingleOrDefaultAsync(filter => filter.Id == id, cancellationToken);
        if (entity is null || (entity.OwnerUserId != userId && !entity.Shared))
        {
            return SavedFilterResult.NotFound;
        }

        if (entity.OwnerUserId != userId)
        {
            return SavedFilterResult.NotOwner;
        }

        db.SalesReportSavedFilters.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return SavedFilterResult.Done;
    }

    private static SalesReportSavedFilter View(SalesReportSavedFilterEntity filter, Guid userId) =>
        new(filter.Id, filter.Name, filter.Query, filter.OwnerName, filter.Shared, filter.OwnerUserId == userId, filter.UpdatedAt);

    private static string? Clean(string? query) => query?.Trim().TrimStart('?');

    private static bool Valid(string? name, string? query) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= MaxName && query is not null && query.Length <= MaxQuery;
}
