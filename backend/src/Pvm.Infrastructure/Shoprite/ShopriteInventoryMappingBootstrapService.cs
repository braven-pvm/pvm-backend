using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pvm.Application.Acumatica;
using Pvm.Domain.Invoices;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Shoprite;

public sealed class ShopriteInventoryMappingBootstrapService(PvmDbContext dbContext)
{
    private const string SystemActor = "system:inventory-mapping-bootstrap";
    private const string BootstrapReason =
        "Exact GTIN and UOM match between Acumatica inventory data and the Shoprite purchase order.";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<ShopriteInventoryMappingBootstrapResult> ResolveKnownMappingsAsync(
        AcumaticaInvoiceDto source,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source.CustomerOrder))
        {
            return ShopriteInventoryMappingBootstrapResult.Empty;
        }

        var purchaseOrders = await dbContext.ShopritePurchaseOrders
            .Include(order => order.Lines)
            .Where(order => order.PurchaseOrderNumber == source.CustomerOrder)
            .ToListAsync(cancellationToken);
        if (purchaseOrders.Count != 1)
        {
            return ShopriteInventoryMappingBootstrapResult.Empty;
        }

        var purchaseOrder = purchaseOrders[0];
        var created = 0;
        var unresolved = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var sourceLine in source.Lines)
        {
            var result = await ResolveLineAsync(sourceLine, purchaseOrder, now, cancellationToken);
            created += result.Created;
            unresolved += result.Unresolved;
        }

        if (created > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new ShopriteInventoryMappingBootstrapResult(created, unresolved);
    }

    private async Task<LineResolution> ResolveLineAsync(
        AcumaticaInvoiceLineDto sourceLine,
        ShopritePurchaseOrderEntity purchaseOrder,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceLine.Gtin))
        {
            return LineResolution.UnresolvedResult;
        }

        var matchingLines = purchaseOrder.Lines
            .Where(line => string.Equals(line.Gtin, sourceLine.Gtin, StringComparison.Ordinal))
            .ToArray();
        if (matchingLines.Length != 1)
        {
            return LineResolution.UnresolvedResult;
        }

        var purchaseOrderLine = matchingLines[0];
        if (string.IsNullOrWhiteSpace(purchaseOrderLine.BuyerItemId)
            || !TryParseUom(sourceLine.Uom, out var acumaticaUom)
            || !TryParseUom(purchaseOrderLine.MeasurementUnitCode, out var shopriteUom)
            || acumaticaUom != shopriteUom)
        {
            return LineResolution.UnresolvedResult;
        }

        var inventoryId = ShopriteInventoryMappingService.Normalize(sourceLine.InventoryId);
        var sourceUom = ShopriteInventoryMappingService.Normalize(sourceLine.Uom);
        var buyerItemId = ShopriteInventoryMappingService.Normalize(purchaseOrderLine.BuyerItemId);
        var gtin = sourceLine.Gtin.Trim();

        var itemMapping = await dbContext.ShopriteItemMappings.SingleOrDefaultAsync(
            mapping => mapping.AcumaticaInventoryId == inventoryId
                && mapping.ShopriteBuyerItemId == buyerItemId,
            cancellationToken);
        var uomMapping = await dbContext.ShopriteUomMappings.SingleOrDefaultAsync(
            mapping => mapping.AcumaticaInventoryId == inventoryId
                && mapping.AcumaticaUom == sourceUom,
            cancellationToken);

        if ((itemMapping is not null
                && (!itemMapping.IsVerified
                    || !string.Equals(itemMapping.Gtin, gtin, StringComparison.Ordinal)))
            || (uomMapping is not null
                && (!uomMapping.IsVerified || uomMapping.ShopriteUom != shopriteUom)))
        {
            return LineResolution.UnresolvedResult;
        }

        var created = 0;
        if (itemMapping is null)
        {
            itemMapping = new ShopriteItemMappingEntity
            {
                Id = Guid.NewGuid(),
                AcumaticaInventoryId = inventoryId,
                ShopriteBuyerItemId = buyerItemId,
                Gtin = gtin,
                IsVerified = true,
                UpdatedBy = SystemActor,
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.ShopriteItemMappings.Add(itemMapping);
            dbContext.AuditEvents.Add(NewAuditEvent(
                "ShopriteItemMapping",
                itemMapping.Id,
                purchaseOrder,
                purchaseOrderLine,
                new
                {
                    itemMapping.AcumaticaInventoryId,
                    itemMapping.ShopriteBuyerItemId,
                    itemMapping.Gtin,
                    itemMapping.IsVerified
                },
                now));
            created++;
        }

        if (uomMapping is null)
        {
            uomMapping = new ShopriteUomMappingEntity
            {
                Id = Guid.NewGuid(),
                AcumaticaInventoryId = inventoryId,
                AcumaticaUom = sourceUom,
                ShopriteUom = shopriteUom,
                IsVerified = true,
                UpdatedBy = SystemActor,
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.ShopriteUomMappings.Add(uomMapping);
            dbContext.AuditEvents.Add(NewAuditEvent(
                "ShopriteUomMapping",
                uomMapping.Id,
                purchaseOrder,
                purchaseOrderLine,
                new
                {
                    uomMapping.AcumaticaInventoryId,
                    uomMapping.AcumaticaUom,
                    uomMapping.ShopriteUom,
                    uomMapping.IsVerified
                },
                now));
            created++;
        }

        return new LineResolution(created, 0);
    }

    private static bool TryParseUom(string? value, out ShopriteMeasurementUnit unit)
        => Enum.TryParse(value, ignoreCase: true, out unit)
            && Enum.IsDefined(unit);

    private static AuditEventEntity NewAuditEvent(
        string entityType,
        Guid entityId,
        ShopritePurchaseOrderEntity purchaseOrder,
        ShopritePurchaseOrderLineEntity purchaseOrderLine,
        object after,
        DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            EntityType = entityType,
            EntityId = entityId.ToString(),
            Action = "created",
            Actor = SystemActor,
            DetailsJson = JsonSerializer.Serialize(new
            {
                reason = BootstrapReason,
                purchaseOrderId = purchaseOrder.Id,
                purchaseOrder.PurchaseOrderNumber,
                purchaseOrderLineId = purchaseOrderLine.Id,
                before = (object?)null,
                after
            }, SerializerOptions),
            CreatedAt = now
        };

    private readonly record struct LineResolution(int Created, int Unresolved)
    {
        public static LineResolution UnresolvedResult => new(0, 1);
    }
}

public sealed record ShopriteInventoryMappingBootstrapResult(int Created, int Unresolved)
{
    public static ShopriteInventoryMappingBootstrapResult Empty { get; } = new(0, 0);
}
