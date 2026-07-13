using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Pvm.Application.Shoprite;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Shoprite;

public sealed class ShopritePurchaseOrderRefreshService(PvmDbContext dbContext)
{
    public async Task<ShopritePurchaseOrderRefreshResult> RefreshAsync(
        ShopritePurchaseOrderBatch batch,
        DateTimeOffset seenAt,
        CancellationToken cancellationToken)
    {
        var orderNumbers = batch.Orders
            .Select(order => order.PurchaseOrderNumber)
            .Where(orderNumber => !string.IsNullOrWhiteSpace(orderNumber))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var existing = await dbContext.ShopritePurchaseOrders
            .Where(order => orderNumbers.Contains(order.PurchaseOrderNumber))
            .ToDictionaryAsync(order => order.PurchaseOrderNumber, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var existingOrderIds = existing.Values
            .Select(order => order.Id)
            .ToArray();
        if (existingOrderIds.Length > 0)
        {
            await dbContext.ShopritePurchaseOrderLines
                .Where(line => existingOrderIds.Contains(line.ShopritePurchaseOrderId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        var created = 0;
        var updated = 0;
        var skipped = 0;

        foreach (var sourceOrder in batch.Orders)
        {
            if (string.IsNullOrWhiteSpace(sourceOrder.PurchaseOrderNumber))
            {
                skipped++;
                continue;
            }

            if (!existing.TryGetValue(sourceOrder.PurchaseOrderNumber, out var entity))
            {
                entity = new ShopritePurchaseOrderEntity
                {
                    Id = Guid.NewGuid(),
                    PurchaseOrderNumber = sourceOrder.PurchaseOrderNumber,
                    DeliveryLocationSource = sourceOrder.DeliveryLocationSource,
                    FirstSeenAt = seenAt,
                    LastSeenAt = seenAt
                };

                dbContext.ShopritePurchaseOrders.Add(entity);
                existing[sourceOrder.PurchaseOrderNumber] = entity;
                created++;
            }
            else
            {
                updated++;
            }

            Apply(sourceOrder, entity, seenAt);
            AddLines(sourceOrder, entity.Id);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ShopritePurchaseOrderRefreshResult(
            Received: batch.Orders.Count,
            Created: created,
            Updated: updated,
            Skipped: skipped,
            RefreshedAt: seenAt);
    }

    private static void Apply(
        ShopritePurchaseOrder source,
        ShopritePurchaseOrderEntity entity,
        DateTimeOffset seenAt)
    {
        entity.OrderHeaderId = source.OrderHeaderId;
        entity.OrderTypeCode = source.OrderTypeCode;
        entity.OrderTypeLabel = source.OrderTypeLabel;
        entity.SupplierGln = source.SupplierGln;
        entity.BuyerGln = source.BuyerGln;
        entity.DeliveryGln = source.DeliveryGln;
        entity.DeliveryLocationCode = source.DeliveryLocationCode;
        entity.DeliveryLocationName = source.DeliveryLocationName;
        entity.DeliveryLocationSource = source.DeliveryLocationSource;
        entity.CurrencyCode = source.CurrencyCode;
        entity.TotalExcludingTax = source.TotalExcludingTax;
        entity.TotalIncludingTax = source.TotalIncludingTax;
        entity.TotalTax = source.TotalTax;
        entity.PayloadHash = Sha256(source.RawOrderJson);
        entity.RawOrderJson = source.RawOrderJson;
        entity.ShopriteCreatedAt = source.CreatedAt;
        entity.ShopriteLastUpdatedAt = source.LastUpdatedAt;
        entity.LastSeenAt = seenAt;
    }

    private void AddLines(ShopritePurchaseOrder source, Guid purchaseOrderId)
    {
        foreach (var line in source.Lines)
        {
            dbContext.ShopritePurchaseOrderLines.Add(new ShopritePurchaseOrderLineEntity
            {
                Id = Guid.NewGuid(),
                ShopritePurchaseOrderId = purchaseOrderId,
                LineNumber = line.LineNumber,
                Gtin = line.Gtin,
                BuyerItemId = line.BuyerItemId,
                BuyerItemDescription = line.BuyerItemDescription,
                SupplierItemId = line.SupplierItemId,
                Description = line.Description,
                RequestedQuantity = line.RequestedQuantity,
                MeasurementUnitCode = line.MeasurementUnitCode,
                NetAmount = line.NetAmount,
                NetPrice = line.NetPrice,
                MonetaryAmountExcludingTaxes = line.MonetaryAmountExcludingTaxes,
                MonetaryAmountIncludingTaxes = line.MonetaryAmountIncludingTaxes
            });
        }
    }

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public sealed record ShopritePurchaseOrderRefreshResult(
    int Received,
    int Created,
    int Updated,
    int Skipped,
    DateTimeOffset RefreshedAt);
