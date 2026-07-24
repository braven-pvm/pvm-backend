using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pvm.Application.Acumatica;
using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Pvm.Infrastructure.Shoprite;

namespace Pvm.Infrastructure.Acumatica;

public sealed class AcumaticaInvoiceCandidateRefreshService(
    IAcumaticaInvoiceClient invoiceClient,
    PvmDbContext dbContext,
    ShopriteInvoiceCandidateMatcher candidateMatcher)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<AcumaticaInvoiceRefreshResult> RefreshAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await dbContext.EnsureShopriteMappingSchemaAsync(cancellationToken);
        var invoices = await invoiceClient.FetchFinalizedInvoicesAsync(cancellationToken);
        var created = 0;
        var updated = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var source in invoices)
        {
            var canonical = AcumaticaInvoiceNormalizer.Normalize(
                source,
                supplierGln: null,
                storeDcGln: null);
            var matched = await candidateMatcher.MatchAndValidateAsync(
                canonical,
                cancellationToken);
            canonical = matched.Invoice;
            var idempotencyKey = BuildIdempotencyKey(canonical);
            var candidate = await dbContext.InvoiceCandidates.SingleOrDefaultAsync(
                entity => entity.AcumaticaInvoiceId == source.Id,
                cancellationToken);

            if (candidate is null)
            {
                candidate = new InvoiceCandidateEntity
                {
                    Id = Guid.NewGuid(),
                    AcumaticaInvoiceId = source.Id,
                    InvoiceNumber = source.InvoiceNumber,
                    CustomerAccount = source.CustomerAccount,
                    IdempotencyKey = idempotencyKey,
                    Status = CandidateStatus(matched.Validation, currentStatus: null),
                    CreatedAt = now,
                    UpdatedAt = now
                };
                dbContext.InvoiceCandidates.Add(candidate);
                created++;
            }
            else
            {
                updated++;
            }

            candidate.AcumaticaInvoiceId = source.Id;
            candidate.InvoiceNumber = source.InvoiceNumber;
            candidate.CustomerAccount = source.CustomerAccount;
            candidate.CustomerLocation = canonical.CustomerLocation;
            candidate.ShopritePurchaseOrderNumber = canonical.ShopritePurchaseOrderNumber;
            candidate.MatchedShopritePurchaseOrderId = matched.MatchedPurchaseOrderId;
            candidate.SupplierGln = canonical.SupplierGln;
            candidate.StoreDcGln = canonical.StoreDcGln;
            candidate.IdempotencyKey = idempotencyKey;
            candidate.Status = CandidateStatus(matched.Validation, candidate.Status);
            candidate.SourceJson = JsonSerializer.Serialize(source, SerializerOptions);
            candidate.CanonicalJson = JsonSerializer.Serialize(canonical, SerializerOptions);
            candidate.ValidationJson = JsonSerializer.Serialize(matched.Validation, SerializerOptions);
            candidate.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new AcumaticaInvoiceRefreshResult(invoices.Count, created, updated);
    }

    private static string CandidateStatus(ValidationResult validation, string? currentStatus)
    {
        if (currentStatus is "Submitted" or "Ambiguous")
        {
            return currentStatus;
        }

        return validation.CanSubmit ? "Ready" : "NeedsReview";
    }

    private static string BuildIdempotencyKey(CanonicalInvoice invoice)
        => $"shoprite-vendorinvoice:{invoice.SupplierGln}:{invoice.StoreDcGln}:{invoice.ShopritePurchaseOrderNumber}:{invoice.InvoiceNumber}";
}

public sealed record AcumaticaInvoiceRefreshResult(int Received, int Created, int Updated);
