using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pvm.Application.Acumatica;
using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Shoprite;

public sealed class ShopriteInvoiceCandidateRevalidationService(
    PvmDbContext dbContext,
    ShopriteInvoiceCandidateMatcher candidateMatcher)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<int> RevalidateForPurchaseOrdersAsync(
        IReadOnlyCollection<string> purchaseOrderNumbers,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        if (purchaseOrderNumbers.Count == 0)
        {
            return 0;
        }

        var changedPurchaseOrderNumbers = purchaseOrderNumbers.ToArray();

        var candidates = await dbContext.InvoiceCandidates
            .Where(candidate =>
                candidate.ShopritePurchaseOrderNumber != null
                && changedPurchaseOrderNumbers.Contains(candidate.ShopritePurchaseOrderNumber)
                && (candidate.Status == "NeedsReview" || candidate.Status == "Ready"))
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            var canonical = RebuildCanonical(candidate);
            if (canonical is null)
            {
                continue;
            }

            var match = await candidateMatcher.MatchAndValidateAsync(canonical, cancellationToken);
            candidate.CanonicalJson = JsonSerializer.Serialize(match.Invoice, SerializerOptions);
            candidate.ValidationJson = JsonSerializer.Serialize(match.Validation, SerializerOptions);
            candidate.MatchedShopritePurchaseOrderId = match.MatchedPurchaseOrderId;
            candidate.SupplierGln = match.Invoice.SupplierGln;
            candidate.StoreDcGln = match.Invoice.StoreDcGln;
            candidate.IdempotencyKey = BuildIdempotencyKey(match.Invoice);
            candidate.Status = CandidateStatus(match.Validation, candidate.Status);
            candidate.UpdatedAt = updatedAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return candidates.Count;
    }

    private static CanonicalInvoice? RebuildCanonical(InvoiceCandidateEntity candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.SourceJson))
        {
            try
            {
                var source = JsonSerializer.Deserialize<AcumaticaInvoiceDto>(
                    candidate.SourceJson,
                    SerializerOptions);
                if (source is not null && !string.IsNullOrWhiteSpace(source.Id))
                {
                    return AcumaticaInvoiceNormalizer.Normalize(source, supplierGln: null, storeDcGln: null);
                }
            }
            catch (JsonException)
            {
                // QA-seeded candidates use Shoprite PO JSON rather than an Acumatica source DTO.
            }
        }

        return string.IsNullOrWhiteSpace(candidate.CanonicalJson)
            ? null
            : JsonSerializer.Deserialize<CanonicalInvoice>(candidate.CanonicalJson, SerializerOptions);
    }

    private static string CandidateStatus(ValidationResult validation, string currentStatus)
        => currentStatus is "Submitted" or "Rejected" or "Ambiguous"
            ? currentStatus
            : validation.CanSubmit ? "Ready" : "NeedsReview";

    private static string BuildIdempotencyKey(CanonicalInvoice invoice)
        => $"shoprite-vendorinvoice:{invoice.SupplierGln}:{invoice.StoreDcGln}:{invoice.ShopritePurchaseOrderNumber}:{invoice.InvoiceNumber}";
}
