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
    ShopriteInvoiceCandidateMatcher candidateMatcher,
    ShopriteInventoryMappingBootstrapService? mappingBootstrapService = null)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly ShopriteInventoryMappingBootstrapService _mappingBootstrapService =
        mappingBootstrapService ?? new ShopriteInventoryMappingBootstrapService(dbContext);

    public Task<AcumaticaInvoiceRefreshResult> RefreshAsync(CancellationToken cancellationToken)
        => RefreshAsync(query: null, cancellationToken);

    public async Task<AcumaticaInvoiceRefreshResult> RefreshAsync(
        AcumaticaInvoiceQuery? query,
        CancellationToken cancellationToken)
    {
        var invoices = await invoiceClient.FetchFinalizedInvoicesAsync(query, cancellationToken);
        return await PersistAsync(invoices, cancellationToken);
    }

    public async Task<AcumaticaInvoiceRefreshResult> RefreshInvoiceAsync(
        string invoiceId,
        CancellationToken cancellationToken)
    {
        var invoice = await invoiceClient.FetchFinalizedInvoiceAsync(invoiceId, cancellationToken);
        if (invoice is not null)
        {
            return await PersistAsync([invoice], cancellationToken);
        }

        var candidate = await dbContext.InvoiceCandidates.SingleOrDefaultAsync(
            item => item.AcumaticaInvoiceId == invoiceId,
            cancellationToken);
        if (candidate is null)
        {
            return new AcumaticaInvoiceRefreshResult(0, 0, 0, 0);
        }

        var validation = new ValidationResult([
            new ValidationIssue(
                "acumatica-invoice-not-finalized",
                "The invoice is no longer available as a finalized Acumatica invoice.",
                ValidationSeverity.Blocking,
                "acumatica")
        ]);
        var validationJson = JsonSerializer.Serialize(validation, SerializerOptions);
        if (candidate.Status == "NeedsReview" && candidate.ValidationJson == validationJson)
        {
            return new AcumaticaInvoiceRefreshResult(0, 0, 0, 1);
        }

        if (candidate.Status is not ("Submitted" or "Rejected" or "Ambiguous"))
        {
            candidate.Status = "NeedsReview";
        }

        candidate.ValidationJson = validationJson;
        candidate.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new AcumaticaInvoiceRefreshResult(0, 0, 1, 0);
    }

    private async Task<AcumaticaInvoiceRefreshResult> PersistAsync(
        IReadOnlyList<AcumaticaInvoiceDto> invoices,
        CancellationToken cancellationToken)
    {
        var created = 0;
        var updated = 0;
        var unchanged = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var source in invoices)
        {
            await _mappingBootstrapService.ResolveKnownMappingsAsync(source, cancellationToken);
            var canonical = AcumaticaInvoiceNormalizer.Normalize(
                source,
                supplierGln: null,
                storeDcGln: null);
            var matched = await candidateMatcher.MatchAndValidateAsync(
                canonical,
                cancellationToken);
            canonical = matched.Invoice;
            var idempotencyKey = BuildIdempotencyKey(canonical);
            var sourceJson = JsonSerializer.Serialize(source, SerializerOptions);
            var canonicalJson = JsonSerializer.Serialize(canonical, SerializerOptions);
            var validationJson = JsonSerializer.Serialize(matched.Validation, SerializerOptions);
            var candidate = await dbContext.InvoiceCandidates.SingleOrDefaultAsync(
                entity => entity.AcumaticaInvoiceId == source.Id,
                cancellationToken);
            var status = CandidateStatus(matched.Validation, candidate?.Status);

            if (candidate is null)
            {
                candidate = new InvoiceCandidateEntity
                {
                    Id = Guid.NewGuid(),
                    AcumaticaInvoiceId = source.Id,
                    InvoiceNumber = source.InvoiceNumber,
                    CustomerAccount = source.CustomerAccount,
                    IdempotencyKey = idempotencyKey,
                    Status = status,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                dbContext.InvoiceCandidates.Add(candidate);
                created++;
            }
            else if (HasSameState(
                candidate,
                source,
                canonical,
                matched.MatchedPurchaseOrderId,
                idempotencyKey,
                status,
                sourceJson,
                canonicalJson,
                validationJson))
            {
                unchanged++;
                continue;
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
            candidate.Status = status;
            candidate.SourceJson = sourceJson;
            candidate.SourceLastModifiedAt = Utc(source.LastModifiedAt);
            candidate.CanonicalJson = canonicalJson;
            candidate.ValidationJson = validationJson;
            candidate.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new AcumaticaInvoiceRefreshResult(invoices.Count, created, updated, unchanged);
    }

    private static bool HasSameState(
        InvoiceCandidateEntity candidate,
        AcumaticaInvoiceDto source,
        CanonicalInvoice canonical,
        Guid? matchedPurchaseOrderId,
        string idempotencyKey,
        string status,
        string sourceJson,
        string canonicalJson,
        string validationJson)
        => candidate.InvoiceNumber == source.InvoiceNumber
            && candidate.CustomerAccount == source.CustomerAccount
            && candidate.CustomerLocation == canonical.CustomerLocation
            && candidate.ShopritePurchaseOrderNumber == canonical.ShopritePurchaseOrderNumber
            && candidate.MatchedShopritePurchaseOrderId == matchedPurchaseOrderId
            && candidate.SupplierGln == canonical.SupplierGln
            && candidate.StoreDcGln == canonical.StoreDcGln
            && candidate.IdempotencyKey == idempotencyKey
            && candidate.Status == status
            && candidate.SourceJson == sourceJson
            && candidate.SourceLastModifiedAt == Utc(source.LastModifiedAt)
            && candidate.CanonicalJson == canonicalJson
            && candidate.ValidationJson == validationJson;

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

    private static DateTimeOffset? Utc(DateTimeOffset? value)
        => value?.ToUniversalTime();
}

public sealed record AcumaticaInvoiceRefreshResult(
    int Received,
    int Created,
    int Updated,
    int Unchanged);
