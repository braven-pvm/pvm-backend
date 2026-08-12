using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pvm.Application.Acumatica;
using Pvm.Domain.Invoices;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Shoprite;

public sealed class ShopriteInventoryMappingService(
    PvmDbContext dbContext,
    ShopriteInvoiceCandidateRevalidationService revalidationService)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ShopriteInventoryMappingView>> ListAsync(
        string? search,
        CancellationToken cancellationToken)
    {
        var candidates = await dbContext.InvoiceCandidates
            .AsNoTracking()
            .OrderByDescending(candidate => candidate.UpdatedAt)
            .ToListAsync(cancellationToken);
        var purchaseOrderIds = candidates
            .Where(candidate => candidate.MatchedShopritePurchaseOrderId is not null)
            .Select(candidate => candidate.MatchedShopritePurchaseOrderId!.Value)
            .Distinct()
            .ToArray();
        var purchaseOrders = await dbContext.ShopritePurchaseOrders
            .AsNoTracking()
            .Include(order => order.Lines)
            .Where(order => purchaseOrderIds.Contains(order.Id))
            .ToDictionaryAsync(order => order.Id, cancellationToken);
        var itemMappings = await dbContext.ShopriteItemMappings
            .AsNoTracking()
            .OrderBy(mapping => mapping.ShopriteBuyerItemId)
            .ToListAsync(cancellationToken);
        var uomMappings = await dbContext.ShopriteUomMappings
            .AsNoTracking()
            .OrderBy(mapping => mapping.AcumaticaUom)
            .ToListAsync(cancellationToken);
        var mappedBuyerItemIds = itemMappings
            .Select(mapping => mapping.ShopriteBuyerItemId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var catalogPurchaseOrders = mappedBuyerItemIds.Length == 0
            ? []
            : await dbContext.ShopritePurchaseOrders
                .AsNoTracking()
                .Include(order => order.Lines)
                .Where(order => order.Lines.Any(line =>
                    line.BuyerItemId != null
                    && mappedBuyerItemIds.Contains(line.BuyerItemId)))
                .ToListAsync(cancellationToken);

        var rows = new Dictionary<InventoryMappingKey, InventoryMappingAccumulator>();
        foreach (var candidate in candidates)
        {
            var source = Deserialize<AcumaticaInvoiceDto>(candidate.SourceJson);
            var canonical = Deserialize<CanonicalInvoice>(candidate.CanonicalJson);
            var sourceLines = source?.Lines ?? [];
            foreach (var sourceLine in sourceLines)
            {
                var key = new InventoryMappingKey(
                    Normalize(sourceLine.InventoryId),
                    Normalize(sourceLine.Uom));
                if (!rows.TryGetValue(key, out var row))
                {
                    row = new InventoryMappingAccumulator(key.InventoryId, key.AcumaticaUom);
                    rows.Add(key, row);
                }

                row.Description ??= sourceLine.Description;
                if (!string.IsNullOrWhiteSpace(sourceLine.Gtin))
                {
                    row.AcumaticaGtins.Add(sourceLine.Gtin.Trim());
                }

                row.AffectedCandidates.Add(candidate.Id);
                var canonicalLine = canonical?.Lines.SingleOrDefault(line =>
                    line.LineNumber == sourceLine.LineNumber);
                if (canonicalLine is null
                    || string.IsNullOrWhiteSpace(canonicalLine.Gtin)
                    || canonicalLine.ShopriteUom is null
                    || !canonicalLine.IsShopriteUomVerified)
                {
                    row.UnresolvedCandidates.Add(candidate.Id);
                }

                if (candidate.MatchedShopritePurchaseOrderId is Guid purchaseOrderId
                    && purchaseOrders.TryGetValue(purchaseOrderId, out var purchaseOrder))
                {
                    foreach (var line in purchaseOrder.Lines)
                    {
                        row.Suggestions.TryAdd(
                            line.Id,
                            new ShopriteInventoryMappingSuggestion(
                                line.Id,
                                purchaseOrder.PurchaseOrderNumber,
                                line.LineNumber,
                                line.BuyerItemId,
                                line.Gtin,
                                line.BuyerItemDescription ?? line.Description));
                    }
                }
            }
        }

        foreach (var mapping in uomMappings)
        {
            var key = new InventoryMappingKey(
                Normalize(mapping.AcumaticaInventoryId),
                Normalize(mapping.AcumaticaUom));
            if (!rows.ContainsKey(key))
            {
                rows.Add(key, new InventoryMappingAccumulator(key.InventoryId, key.AcumaticaUom));
            }
        }

        foreach (var row in rows.Values)
        {
            var rowBuyerItemIds = itemMappings
                .Where(mapping => Normalize(mapping.AcumaticaInventoryId) == row.InventoryId)
                .Select(mapping => mapping.ShopriteBuyerItemId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var catalogItems = catalogPurchaseOrders
                .SelectMany(order => order.Lines
                    .Where(line => line.BuyerItemId is not null && rowBuyerItemIds.Contains(line.BuyerItemId))
                    .Select(line => new { Order = order, Line = line }))
                .GroupBy(item => new
                {
                    BuyerItemId = Normalize(item.Line.BuyerItemId!),
                    Gtin = item.Line.Gtin?.Trim() ?? string.Empty
                })
                .Select(group => group
                    .OrderByDescending(item => item.Order.LastSeenAt)
                    .ThenByDescending(item => item.Line.LineNumber)
                    .First())
                .ToArray();

            foreach (var item in catalogItems)
            {
                row.Description ??= item.Line.BuyerItemDescription ?? item.Line.Description;
                row.Suggestions.TryAdd(
                    item.Line.Id,
                    new ShopriteInventoryMappingSuggestion(
                        item.Line.Id,
                        item.Order.PurchaseOrderNumber,
                        item.Line.LineNumber,
                        item.Line.BuyerItemId,
                        item.Line.Gtin,
                        item.Line.BuyerItemDescription ?? item.Line.Description));
            }
        }

        var views = rows.Values.Select(row =>
        {
            var inventoryItemMappings = itemMappings
                .Where(mapping => Normalize(mapping.AcumaticaInventoryId) == row.InventoryId)
                .Select(mapping => new ShopriteInventoryItemMappingView(
                    mapping.Id,
                    mapping.ShopriteBuyerItemId,
                    mapping.Gtin,
                    mapping.IsVerified,
                    mapping.UpdatedBy,
                    mapping.UpdatedAt))
                .ToArray();
            var uomMapping = uomMappings.SingleOrDefault(mapping =>
                Normalize(mapping.AcumaticaInventoryId) == row.InventoryId
                && Normalize(mapping.AcumaticaUom) == row.AcumaticaUom);
            var verifiedBuyerItemIds = inventoryItemMappings
                .Where(mapping => mapping.IsVerified)
                .Select(mapping => mapping.ShopriteBuyerItemId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var relevantSuggestions = verifiedBuyerItemIds.Count == 0
                ? row.Suggestions.Values
                : row.Suggestions.Values.Where(suggestion =>
                    suggestion.ShopriteBuyerItemId is not null
                    && verifiedBuyerItemIds.Contains(suggestion.ShopriteBuyerItemId));
            var distinctSuggestions = relevantSuggestions
                .GroupBy(suggestion => new
                {
                    BuyerItemId = Normalize(suggestion.ShopriteBuyerItemId ?? string.Empty),
                    Gtin = suggestion.Gtin?.Trim() ?? string.Empty
                })
                .Select(group => group
                    .OrderByDescending(suggestion => suggestion.PurchaseOrderNumber, StringComparer.Ordinal)
                    .ThenByDescending(suggestion => suggestion.LineNumber)
                    .First());

            return new ShopriteInventoryMappingView(
                row.InventoryId,
                row.Description,
                row.AcumaticaUom,
                row.AcumaticaGtins.Order(StringComparer.Ordinal).ToArray(),
                inventoryItemMappings,
                uomMapping is null
                    ? null
                    : new ShopriteInventoryUomMappingView(
                        uomMapping.Id,
                        uomMapping.ShopriteUom,
                        uomMapping.IsVerified,
                        uomMapping.UpdatedBy,
                        uomMapping.UpdatedAt),
                distinctSuggestions
                    .OrderBy(suggestion => suggestion.PurchaseOrderNumber, StringComparer.Ordinal)
                    .ThenBy(suggestion => suggestion.LineNumber)
                    .ToArray(),
                row.AffectedCandidates.Count,
                row.UnresolvedCandidates.Count);
        });

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            views = views.Where(view =>
                view.InventoryId.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (view.Description?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || view.AcumaticaGtins.Any(gtin => gtin.Contains(term, StringComparison.OrdinalIgnoreCase))
                || view.ItemMappings.Any(mapping =>
                    mapping.ShopriteBuyerItemId.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || mapping.Gtin.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        return views
            .OrderByDescending(view => view.UnresolvedCandidateCount)
            .ThenBy(view => view.InventoryId, StringComparer.Ordinal)
            .ThenBy(view => view.AcumaticaUom, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<IReadOnlyList<ShopriteCatalogItemView>> ListShopriteCatalogAsync(
        string? search,
        CancellationToken cancellationToken)
    {
        var purchaseOrders = await dbContext.ShopritePurchaseOrders
            .AsNoTracking()
            .Include(order => order.Lines)
            .OrderByDescending(order => order.LastSeenAt)
            .ToListAsync(cancellationToken);
        var mappings = await dbContext.ShopriteItemMappings
            .AsNoTracking()
            .Where(mapping => mapping.IsVerified)
            .ToListAsync(cancellationToken);

        var catalog = purchaseOrders
            .SelectMany(order => order.Lines.Select(line => new { Order = order, Line = line }))
            .Where(item => !string.IsNullOrWhiteSpace(item.Line.BuyerItemId)
                && !string.IsNullOrWhiteSpace(item.Line.Gtin))
            .GroupBy(item => Normalize(item.Line.BuyerItemId!))
            .Select(group =>
            {
                var latest = group
                    .OrderByDescending(item => item.Order.LastSeenAt)
                    .ThenByDescending(item => item.Line.LineNumber)
                    .First();
                var mappedInventoryIds = mappings
                    .Where(mapping => Normalize(mapping.ShopriteBuyerItemId) == group.Key)
                    .Select(mapping => Normalize(mapping.AcumaticaInventoryId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new ShopriteCatalogItemView(
                    group.Key,
                    latest.Line.BuyerItemDescription ?? latest.Line.Description,
                    group.Select(item => item.Line.Gtin!.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    group.Select(item => item.Line.SupplierItemId)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value!.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    group.Select(item => item.Line.MeasurementUnitCode)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => Normalize(value!))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    group.Select(item => item.Order.Id).Distinct().Count(),
                    latest.Order.PurchaseOrderNumber,
                    latest.Line.Id,
                    mappedInventoryIds);
            });

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            catalog = catalog.Where(item =>
                item.ShopriteBuyerItemId.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (item.Description?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || item.Gtins.Any(gtin => gtin.Contains(term, StringComparison.OrdinalIgnoreCase))
                || item.SupplierItemIds.Any(id => id.Contains(term, StringComparison.OrdinalIgnoreCase))
                || item.MappedInventoryIds.Any(id => id.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        return catalog
            .OrderBy(item => item.IsMapped)
            .ThenBy(item => item.ShopriteBuyerItemId, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<ShopriteInventoryMappingSaveResult> SaveAsync(
        string inventoryId,
        string acumaticaUom,
        Guid purchaseOrderLineId,
        ShopriteMeasurementUnit shopriteUom,
        string actor,
        string reason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inventoryId) || string.IsNullOrWhiteSpace(acumaticaUom))
        {
            return Failure(
                ShopriteInventoryMappingSaveStatus.InvalidInventoryKey,
                "Acumatica inventory ID and UOM are required.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Failure(
                ShopriteInventoryMappingSaveStatus.InvalidReason,
                "A reason is required for inventory mapping changes.");
        }

        var purchaseOrderLine = await dbContext.ShopritePurchaseOrderLines
            .AsNoTracking()
            .SingleOrDefaultAsync(line => line.Id == purchaseOrderLineId, cancellationToken);
        if (purchaseOrderLine is null)
        {
            return Failure(
                ShopriteInventoryMappingSaveStatus.PurchaseOrderLineNotFound,
                "Shoprite PO line was not found.");
        }

        if (string.IsNullOrWhiteSpace(purchaseOrderLine.BuyerItemId)
            || string.IsNullOrWhiteSpace(purchaseOrderLine.Gtin))
        {
            return Failure(
                ShopriteInventoryMappingSaveStatus.PurchaseOrderLineMissingIdentifiers,
                "Selected Shoprite PO line must include both buyer item ID and GTIN.");
        }

        var normalizedInventoryId = Normalize(inventoryId);
        var normalizedAcumaticaUom = Normalize(acumaticaUom);
        var buyerItemId = Normalize(purchaseOrderLine.BuyerItemId);
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var conflictingMappings = await dbContext.ShopriteItemMappings
            .Where(mapping => mapping.IsVerified
                && (mapping.ShopriteBuyerItemId == buyerItemId
                    || mapping.AcumaticaInventoryId == normalizedInventoryId)
                && !(mapping.ShopriteBuyerItemId == buyerItemId
                    && mapping.AcumaticaInventoryId == normalizedInventoryId))
            .ToListAsync(cancellationToken);
        var affectedInventoryIds = conflictingMappings
            .Select(mapping => mapping.AcumaticaInventoryId)
            .Append(normalizedInventoryId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var conflictingMapping in conflictingMappings)
        {
            dbContext.AuditEvents.Add(NewAuditEvent(
                "ShopriteItemMapping",
                conflictingMapping.Id,
                "reassigned",
                actor,
                reason.Trim(),
                new
                {
                    conflictingMapping.AcumaticaInventoryId,
                    conflictingMapping.ShopriteBuyerItemId,
                    conflictingMapping.Gtin,
                    conflictingMapping.IsVerified
                },
                new
                {
                    AcumaticaInventoryId = normalizedInventoryId,
                    ShopriteBuyerItemId = buyerItemId,
                    Gtin = purchaseOrderLine.Gtin.Trim(),
                    IsVerified = true
                },
                now));
        }

        dbContext.ShopriteItemMappings.RemoveRange(conflictingMappings);

        var itemMapping = await dbContext.ShopriteItemMappings.SingleOrDefaultAsync(
            mapping => mapping.AcumaticaInventoryId == normalizedInventoryId
                && mapping.ShopriteBuyerItemId == buyerItemId,
            cancellationToken);
        var itemBefore = itemMapping is null
            ? null
            : new { itemMapping.Gtin, itemMapping.IsVerified, itemMapping.UpdatedBy };
        if (itemMapping is null)
        {
            itemMapping = new ShopriteItemMappingEntity
            {
                Id = Guid.NewGuid(),
                AcumaticaInventoryId = normalizedInventoryId,
                ShopriteBuyerItemId = buyerItemId,
                Gtin = purchaseOrderLine.Gtin.Trim(),
                IsVerified = true,
                UpdatedBy = actor,
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.ShopriteItemMappings.Add(itemMapping);
        }
        else
        {
            itemMapping.Gtin = purchaseOrderLine.Gtin.Trim();
            itemMapping.IsVerified = true;
            itemMapping.UpdatedBy = actor;
            itemMapping.UpdatedAt = now;
        }

        var uomMapping = await dbContext.ShopriteUomMappings.SingleOrDefaultAsync(
            mapping => mapping.AcumaticaInventoryId == normalizedInventoryId
                && mapping.AcumaticaUom == normalizedAcumaticaUom,
            cancellationToken);
        var uomBefore = uomMapping is null
            ? null
            : new { uomMapping.ShopriteUom, uomMapping.IsVerified, uomMapping.UpdatedBy };
        if (uomMapping is null)
        {
            uomMapping = new ShopriteUomMappingEntity
            {
                Id = Guid.NewGuid(),
                AcumaticaInventoryId = normalizedInventoryId,
                AcumaticaUom = normalizedAcumaticaUom,
                ShopriteUom = shopriteUom,
                IsVerified = true,
                UpdatedBy = actor,
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.ShopriteUomMappings.Add(uomMapping);
        }
        else
        {
            uomMapping.ShopriteUom = shopriteUom;
            uomMapping.IsVerified = true;
            uomMapping.UpdatedBy = actor;
            uomMapping.UpdatedAt = now;
        }

        dbContext.AuditEvents.AddRange(
            NewAuditEvent(
                "ShopriteItemMapping",
                itemMapping.Id,
                itemBefore is null ? "created" : "updated",
                actor,
                reason.Trim(),
                itemBefore,
                new
                {
                    purchaseOrderLineId,
                    purchaseOrderLine.ShopritePurchaseOrderId,
                    itemMapping.AcumaticaInventoryId,
                    itemMapping.ShopriteBuyerItemId,
                    itemMapping.Gtin,
                    itemMapping.IsVerified
                },
                now),
            NewAuditEvent(
                "ShopriteUomMapping",
                uomMapping.Id,
                uomBefore is null ? "created" : "updated",
                actor,
                reason.Trim(),
                uomBefore,
                new
                {
                    purchaseOrderLineId,
                    purchaseOrderLine.ShopritePurchaseOrderId,
                    uomMapping.AcumaticaInventoryId,
                    uomMapping.AcumaticaUom,
                    uomMapping.ShopriteUom,
                    uomMapping.IsVerified
                },
                now));

        await dbContext.SaveChangesAsync(cancellationToken);
        var revalidated = await revalidationService.RevalidateForInventoryItemsAsync(
            affectedInventoryIds,
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ShopriteInventoryMappingSaveResult(
            ShopriteInventoryMappingSaveStatus.Saved,
            revalidated,
            "Inventory mapping saved and affected invoice candidates revalidated.");
    }

    public static string Normalize(string value)
        => value.Trim().ToUpperInvariant();

    private static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static AuditEventEntity NewAuditEvent(
        string entityType,
        Guid entityId,
        string action,
        string actor,
        string reason,
        object? before,
        object after,
        DateTimeOffset createdAt)
        => new()
        {
            Id = Guid.NewGuid(),
            EntityType = entityType,
            EntityId = entityId.ToString(),
            Action = action,
            Actor = actor,
            DetailsJson = JsonSerializer.Serialize(new { reason, before, after }, SerializerOptions),
            CreatedAt = createdAt
        };

    private static ShopriteInventoryMappingSaveResult Failure(
        ShopriteInventoryMappingSaveStatus status,
        string message)
        => new(status, 0, message);

    private sealed class InventoryMappingAccumulator(string inventoryId, string acumaticaUom)
    {
        public string InventoryId { get; } = inventoryId;
        public string AcumaticaUom { get; } = acumaticaUom;
        public string? Description { get; set; }
        public HashSet<string> AcumaticaGtins { get; } = new(StringComparer.Ordinal);
        public HashSet<Guid> AffectedCandidates { get; } = [];
        public HashSet<Guid> UnresolvedCandidates { get; } = [];
        public Dictionary<Guid, ShopriteInventoryMappingSuggestion> Suggestions { get; } = [];
    }

    private sealed record InventoryMappingKey(string InventoryId, string AcumaticaUom);
}

public sealed record ShopriteInventoryMappingView(
    string InventoryId,
    string? Description,
    string AcumaticaUom,
    IReadOnlyList<string> AcumaticaGtins,
    IReadOnlyList<ShopriteInventoryItemMappingView> ItemMappings,
    ShopriteInventoryUomMappingView? UomMapping,
    IReadOnlyList<ShopriteInventoryMappingSuggestion> Suggestions,
    int AffectedCandidateCount,
    int UnresolvedCandidateCount);

public sealed record ShopriteInventoryItemMappingView(
    Guid Id,
    string ShopriteBuyerItemId,
    string Gtin,
    bool IsVerified,
    string UpdatedBy,
    DateTimeOffset UpdatedAt);

public sealed record ShopriteInventoryUomMappingView(
    Guid Id,
    ShopriteMeasurementUnit ShopriteUom,
    bool IsVerified,
    string UpdatedBy,
    DateTimeOffset UpdatedAt);

public sealed record ShopriteInventoryMappingSuggestion(
    Guid PurchaseOrderLineId,
    string PurchaseOrderNumber,
    int LineNumber,
    string? ShopriteBuyerItemId,
    string? Gtin,
    string? Description);

public sealed record ShopriteCatalogItemView(
    string ShopriteBuyerItemId,
    string? Description,
    IReadOnlyList<string> Gtins,
    IReadOnlyList<string> SupplierItemIds,
    IReadOnlyList<string> MeasurementUnitCodes,
    int PurchaseOrderCount,
    string LatestPurchaseOrderNumber,
    Guid RepresentativePurchaseOrderLineId,
    IReadOnlyList<string> MappedInventoryIds)
{
    public bool IsMapped => MappedInventoryIds.Count > 0;
}

public sealed record ShopriteInventoryMappingSaveResult(
    ShopriteInventoryMappingSaveStatus Status,
    int RevalidatedCandidateCount,
    string Message);

public enum ShopriteInventoryMappingSaveStatus
{
    Saved,
    InvalidInventoryKey,
    InvalidReason,
    PurchaseOrderLineNotFound,
    PurchaseOrderLineMissingIdentifiers
}
