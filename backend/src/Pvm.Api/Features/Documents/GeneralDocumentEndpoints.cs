using Microsoft.EntityFrameworkCore;
using Pvm.Application.Documents;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Api.Features.Documents;

/// <summary>
/// The workbench's General scans page (design 6.12), under the read policy:
/// <list type="bullet">
///   <item><c>GET /api/documents/general?q=&amp;device=&amp;staff=&amp;from=&amp;to=&amp;page=</c> — the general
///   documents, newest first, pages of 50. <c>q</c> matches the typed title and note, the automatic
///   title, the summary and the search words, ignoring case. <c>device</c> and <c>staff</c> are IDs
///   from the filters endpoint. Every state is listed.</item>
///   <item><c>GET /api/documents/general/filters</c> — the scanner devices and staff names that
///   uploaded general documents, by their current names, for the list's filters. (The scanner
///   admin endpoints are for Admins only.)</item>
/// </list>
/// For now every user with the read policy sees every general document.
/// </summary>
public static class GeneralDocumentEndpoints
{
    public const int PageSize = DocumentReviewEndpoints.PageSize;

    /// <summary>The longest search text a request may send.</summary>
    public const int MaxQueryLength = 200;

    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/general", ListAsync).RequireAuthorization("Invoices.Read");
        group.MapGet("/general/filters", FiltersAsync).RequireAuthorization("Invoices.Read");
    }

    private static async Task<IResult> ListAsync(
        PvmDbContext dbContext,
        string? q,
        string? device,
        string? staff,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? page,
        CancellationToken cancellationToken)
    {
        var query = dbContext.CapturedDocuments
            .AsNoTracking()
            .Where(item => item.DocumentType == DocumentTypeCodes.General);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();
            if (term.Length > MaxQueryLength)
            {
                return DocumentReviewEndpoints.BadRequest($"The query 'q' must be at most {MaxQueryLength} characters.");
            }

            query = query.Where(item => item.SearchText != null && item.SearchText.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(device))
        {
            if (!Guid.TryParse(device, out var deviceId))
            {
                return DocumentReviewEndpoints.BadRequest("The filter 'device' must be a scanner device ID.");
            }

            query = query.Where(item => item.ScannerDeviceId == deviceId);
        }

        if (!string.IsNullOrWhiteSpace(staff))
        {
            if (!Guid.TryParse(staff, out var staffId))
            {
                return DocumentReviewEndpoints.BadRequest("The filter 'staff' must be a staff name ID.");
            }

            query = query.Where(item => item.ScannerStaffId == staffId);
        }

        if (from is not null)
        {
            var fromUtc = from.Value.ToUniversalTime();
            query = query.Where(item => item.UploadedAt >= fromUtc);
        }

        if (to is not null)
        {
            var toUtc = to.Value.ToUniversalTime();
            query = query.Where(item => item.UploadedAt < toUtc);
        }

        var pageNumber = Math.Max(page ?? 1, 1);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(item => item.UploadedAt)
            .ThenByDescending(item => item.Id)
            .Skip((pageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync(cancellationToken);

        var deviceNames = await DocumentReviewEndpoints.DeviceNamesAsync(dbContext, items, cancellationToken);
        return Results.Ok(new
        {
            items = items.Select(item => Item(item, deviceNames)),
            page = pageNumber,
            pageSize = PageSize,
            total
        });
    }

    private static async Task<IResult> FiltersAsync(PvmDbContext dbContext, CancellationToken cancellationToken)
    {
        var general = dbContext.CapturedDocuments
            .AsNoTracking()
            .Where(item => item.DocumentType == DocumentTypeCodes.General);

        var deviceIds = await general
            .Where(item => item.ScannerDeviceId != null)
            .Select(item => item.ScannerDeviceId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        var devices = await dbContext.ScannerDevices
            .AsNoTracking()
            .Where(item => deviceIds.Contains(item.Id))
            .Select(item => new { id = item.Id, name = item.Name })
            .ToListAsync(cancellationToken);

        // The staff name as it is now; a name that was removed keeps the one stored at upload.
        var staffUsed = await general
            .Where(item => item.ScannerStaffId != null)
            .Select(item => new { Id = item.ScannerStaffId!.Value, item.ScannerStaffName })
            .Distinct()
            .ToListAsync(cancellationToken);
        var staffIds = staffUsed.Select(item => item.Id).Distinct().ToList();
        var currentNames = await dbContext.ScannerStaff
            .AsNoTracking()
            .Where(item => staffIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
        var staff = staffIds
            .Select(id => new
            {
                id,
                name = currentNames.TryGetValue(id, out var name)
                    ? name
                    : staffUsed.First(item => item.Id == id).ScannerStaffName ?? "Unknown"
            })
            .ToList();

        return Results.Ok(new
        {
            devices = devices.OrderBy(item => item.name, StringComparer.OrdinalIgnoreCase),
            staff = staff.OrderBy(item => item.name, StringComparer.OrdinalIgnoreCase)
        });
    }

    private static object Item(CapturedDocumentEntity item, IReadOnlyDictionary<Guid, string> deviceNames) => new
    {
        id = item.Id,
        documentType = item.DocumentType,
        state = item.State.ToString(),
        title = item.Title,
        note = item.Note,
        autoTitle = item.AutoTitle,
        summary = item.Summary,
        documentDate = item.DocumentDate,
        searchWords = item.SearchWords ?? [],
        hasThumbnail = item.ThumbnailBlobPath != null,
        pageCount = item.PageCount,
        uploadedBy = item.UploadedBy,
        uploadedAt = item.UploadedAt,
        scannerDeviceId = item.ScannerDeviceId,
        scannerDeviceName = DocumentReviewEndpoints.DeviceName(item, deviceNames),
        scannerStaffId = item.ScannerStaffId,
        scannerStaffName = item.ScannerStaffName
    };
}
