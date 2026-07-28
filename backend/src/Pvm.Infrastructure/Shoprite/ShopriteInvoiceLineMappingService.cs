using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Shoprite;

public sealed class ShopriteInvoiceLineMappingService(
    PvmDbContext dbContext,
    ShopriteInvoiceCandidateMatcher candidateMatcher)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<ShopriteLineMappingSaveResult> SaveAsync(
        Guid candidateId,
        int lineNumber,
        Guid purchaseOrderLineId,
        ShopriteMeasurementUnit shopriteUom,
        string actor,
        CancellationToken cancellationToken)
    {
        var candidate = await dbContext.InvoiceCandidates
            .SingleOrDefaultAsync(entity => entity.Id == candidateId, cancellationToken);
        if (candidate is null)
        {
            return Failure(ShopriteLineMappingSaveStatus.CandidateNotFound, "Invoice candidate not found.");
        }

        if (candidate.Status is "Submitted" or "Ambiguous")
        {
            return Failure(
                ShopriteLineMappingSaveStatus.CandidateLocked,
                $"Mappings cannot be changed while the candidate status is {candidate.Status}.");
        }

        var canonical = Deserialize<CanonicalInvoice>(candidate.CanonicalJson);
        var candidateLine = canonical?.Lines.SingleOrDefault(line => line.LineNumber == lineNumber);
        if (canonical is null || candidateLine is null)
        {
            return Failure(
                ShopriteLineMappingSaveStatus.CandidateLineNotFound,
                "Invoice candidate line was not found.");
        }

        if (candidate.MatchedShopritePurchaseOrderId is null)
        {
            return Failure(
                ShopriteLineMappingSaveStatus.NoMatchedPurchaseOrder,
                "Invoice candidate has no matched Shoprite PO.");
        }

        var purchaseOrderLine = await dbContext.ShopritePurchaseOrderLines
            .AsNoTracking()
            .SingleOrDefaultAsync(
                line => line.Id == purchaseOrderLineId
                    && line.ShopritePurchaseOrderId == candidate.MatchedShopritePurchaseOrderId,
                cancellationToken);
        if (purchaseOrderLine is null)
        {
            return Failure(
                ShopriteLineMappingSaveStatus.PurchaseOrderLineNotFound,
                "Selected line does not belong to the matched Shoprite PO.");
        }

        if (string.IsNullOrWhiteSpace(purchaseOrderLine.BuyerItemId)
            || string.IsNullOrWhiteSpace(purchaseOrderLine.Gtin))
        {
            return Failure(
                ShopriteLineMappingSaveStatus.PurchaseOrderLineMissingIdentifiers,
                "Selected Shoprite PO line must include both buyer item ID and GTIN.");
        }

        var now = DateTimeOffset.UtcNow;
        var inventoryId = ShopriteInvoiceCandidateMatcher.Normalize(candidateLine.AcumaticaInventoryId);
        var acumaticaUom = ShopriteInvoiceCandidateMatcher.Normalize(candidateLine.AcumaticaUom);
        var buyerItemId = ShopriteInvoiceCandidateMatcher.Normalize(purchaseOrderLine.BuyerItemId);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var itemMapping = await dbContext.ShopriteItemMappings.SingleOrDefaultAsync(
            mapping => mapping.AcumaticaInventoryId == inventoryId
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
                AcumaticaInventoryId = inventoryId,
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
            mapping => mapping.AcumaticaInventoryId == inventoryId
                && mapping.AcumaticaUom == acumaticaUom,
            cancellationToken);
        var uomBefore = uomMapping is null
            ? null
            : new { uomMapping.ShopriteUom, uomMapping.IsVerified, uomMapping.UpdatedBy };
        if (uomMapping is null)
        {
            uomMapping = new ShopriteUomMappingEntity
            {
                Id = Guid.NewGuid(),
                AcumaticaInventoryId = inventoryId,
                AcumaticaUom = acumaticaUom,
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
            NewMappingAuditEvent(
                "ShopriteItemMapping",
                itemMapping.Id,
                itemBefore is null ? "created" : "updated",
                actor,
                itemBefore,
                new
                {
                    candidateId,
                    lineNumber,
                    purchaseOrderLineId,
                    itemMapping.AcumaticaInventoryId,
                    itemMapping.ShopriteBuyerItemId,
                    itemMapping.Gtin,
                    itemMapping.IsVerified
                },
                now),
            NewMappingAuditEvent(
                "ShopriteUomMapping",
                uomMapping.Id,
                uomBefore is null ? "created" : "updated",
                actor,
                uomBefore,
                new
                {
                    candidateId,
                    lineNumber,
                    uomMapping.AcumaticaInventoryId,
                    uomMapping.AcumaticaUom,
                    uomMapping.ShopriteUom,
                    uomMapping.IsVerified
                },
                now));

        await dbContext.SaveChangesAsync(cancellationToken);

        var canonicalForValidation = canonical with
        {
            Lines = canonical.Lines.Select(line =>
                line.LineNumber == lineNumber
                    ? line with
                    {
                        Gtin = purchaseOrderLine.Gtin.Trim(),
                        ShopriteUom = shopriteUom,
                        IsShopriteUomVerified = true
                    }
                    : line).ToArray()
        };
        var match = await candidateMatcher.MatchAndValidateAsync(
            canonicalForValidation,
            cancellationToken);
        ApplyMatch(candidate, match, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ShopriteLineMappingSaveResult(
            ShopriteLineMappingSaveStatus.Saved,
            candidate,
            "Mapping saved and invoice candidate revalidated.");
    }

    private static void ApplyMatch(
        InvoiceCandidateEntity candidate,
        ShopriteInvoiceMatchResult match,
        DateTimeOffset updatedAt)
    {
        candidate.ValidationJson = JsonSerializer.Serialize(match.Validation, SerializerOptions);
        candidate.CanonicalJson = JsonSerializer.Serialize(match.Invoice, SerializerOptions);
        candidate.MatchedShopritePurchaseOrderId = match.MatchedPurchaseOrderId;
        candidate.SupplierGln = match.Invoice.SupplierGln;
        candidate.StoreDcGln = match.Invoice.StoreDcGln;
        candidate.IdempotencyKey = BuildIdempotencyKey(match.Invoice);
        candidate.Status = CandidateStatus(match.Validation, candidate.Status);
        candidate.UpdatedAt = updatedAt;
    }

    private static string CandidateStatus(ValidationResult validation, string? currentStatus)
    {
        if (currentStatus is "Submitted" or "Rejected" or "Ambiguous")
        {
            return currentStatus;
        }

        return validation.CanSubmit ? "Ready" : "NeedsReview";
    }

    private static string BuildIdempotencyKey(CanonicalInvoice invoice)
        => $"shoprite-vendorinvoice:{invoice.SupplierGln}:{invoice.StoreDcGln}:{invoice.ShopritePurchaseOrderNumber}:{invoice.InvoiceNumber}";

    private static T? Deserialize<T>(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? default
            : JsonSerializer.Deserialize<T>(json, SerializerOptions);

    private static AuditEventEntity NewMappingAuditEvent(
        string entityType,
        Guid entityId,
        string action,
        string actor,
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
            DetailsJson = JsonSerializer.Serialize(new { before, after }, SerializerOptions),
            CreatedAt = createdAt
        };

    private static ShopriteLineMappingSaveResult Failure(
        ShopriteLineMappingSaveStatus status,
        string message)
        => new(status, null, message);
}

public enum ShopriteLineMappingSaveStatus
{
    Saved,
    CandidateNotFound,
    CandidateLocked,
    CandidateLineNotFound,
    NoMatchedPurchaseOrder,
    PurchaseOrderLineNotFound,
    PurchaseOrderLineMissingIdentifiers
}

public sealed record ShopriteLineMappingSaveResult(
    ShopriteLineMappingSaveStatus Status,
    InvoiceCandidateEntity? Candidate,
    string Message);
