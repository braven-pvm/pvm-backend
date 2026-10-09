using Microsoft.EntityFrameworkCore;
using Pvm.Application.Shoprite;
using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;
using Pvm.Infrastructure.Persistence;

namespace Pvm.Infrastructure.Shoprite;

/// <summary>
/// Completes an Acumatica invoice for Shoprite from reference data, then validates it.
///
/// The invoice no longer reads the Shoprite order. Shoprite marks an order as downloaded the
/// moment anything reads it, and PVM's people work orders on the Shoprite portal, so reading
/// orders took them out of their hands. The store or distribution centre now comes from the
/// Acumatica customer, and each line's GTIN from the item, through
/// <see cref="ShopriteInvoiceReferenceEnricher"/>.
/// </summary>
public sealed class ShopriteInvoiceCandidateMatcher(PvmDbContext dbContext)
{
    // Candidate states in which an invoice was sent to Shoprite, or may have been.
    private static readonly string[] SentStatuses = ["Submitted", "InProgress", "Ambiguous"];

    public async Task<ShopriteInvoiceMatchResult> MatchAndValidateAsync(
        CanonicalInvoice invoice,
        CancellationToken cancellationToken)
    {
        var branchCode = Normalize(invoice.CustomerAccount);
        var location = await dbContext.ShopriteDeliveryLocations
            .AsNoTracking()
            .Where(entity => entity.BranchCode == branchCode)
            .Select(entity => new ShopriteDeliveryLocation(
                entity.BranchCode,
                entity.Gln,
                entity.Name,
                entity.LocationType,
                entity.IsVerified))
            .SingleOrDefaultAsync(cancellationToken);

        var inventoryIds = invoice.Lines
            .Select(line => Normalize(line.AcumaticaInventoryId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var tradeItems = await dbContext.ShopriteTradeItems
            .AsNoTracking()
            .Where(entity => inventoryIds.Contains(entity.AcumaticaInventoryId))
            .Select(entity => new ShopriteTradeItem(
                entity.AcumaticaInventoryId,
                entity.AcumaticaUom,
                entity.DeliversTo,
                entity.Gtin,
                entity.AcumaticaUnitsPerShopriteUnit,
                entity.ShopritePackSize,
                entity.ShopriteUom,
                entity.IsVerified))
            .ToListAsync(cancellationToken);

        var alreadySubmitted = string.IsNullOrWhiteSpace(invoice.ShopritePurchaseOrderNumber)
            ? []
            : await dbContext.InvoiceCandidates
                .AsNoTracking()
                .Where(candidate =>
                    candidate.ShopritePurchaseOrderNumber == invoice.ShopritePurchaseOrderNumber
                    && candidate.AcumaticaInvoiceId != invoice.AcumaticaInvoiceId
                    && SentStatuses.Contains(candidate.Status))
                .OrderBy(candidate => candidate.InvoiceNumber)
                .Select(candidate => candidate.InvoiceNumber)
                .ToArrayAsync(cancellationToken);

        var enrichment = ShopriteInvoiceReferenceEnricher.Enrich(invoice, location, tradeItems, alreadySubmitted);
        var baseValidation = ShopriteInvoiceValidator.Validate(enrichment.Invoice, ShopriteValidationEnvironment.Qa);
        var validation = enrichment.Issues.Count == 0
            ? baseValidation
            : new ValidationResult(baseValidation.Issues.Concat(enrichment.Issues).ToArray());

        return new ShopriteInvoiceMatchResult(enrichment.Invoice, MatchedPurchaseOrderId: null, validation);
    }

    public static string Normalize(string value)
        => ShopriteInvoiceReferenceEnricher.Normalize(value);
}

public sealed record ShopriteInvoiceMatchResult(
    CanonicalInvoice Invoice,
    Guid? MatchedPurchaseOrderId,
    ValidationResult Validation);
