using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pvm.Application.Acumatica;
using Pvm.Application.Shoprite;
using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Acumatica;

public sealed class AcumaticaInvoiceCandidateRefreshService(
    IAcumaticaInvoiceClient invoiceClient,
    PvmDbContext dbContext)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<AcumaticaInvoiceRefreshResult> RefreshAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
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
            var matched = await MatchPurchaseOrderAndValidateAsync(
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

    private async Task<MatchedInvoiceValidation> MatchPurchaseOrderAndValidateAsync(
        CanonicalInvoice invoice,
        CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();
        Guid? matchedPurchaseOrderId = null;

        if (!string.IsNullOrWhiteSpace(invoice.ShopritePurchaseOrderNumber))
        {
            var matches = await dbContext.ShopritePurchaseOrders
                .AsNoTracking()
                .Include(order => order.Lines)
                .Where(order => order.PurchaseOrderNumber == invoice.ShopritePurchaseOrderNumber)
                .ToListAsync(cancellationToken);

            if (matches.Count == 0)
            {
                issues.Add(new ValidationIssue(
                    "missing-local-shoprite-po",
                    $"Shoprite PO {invoice.ShopritePurchaseOrderNumber} has not been loaded into the local PO inbox.",
                    ValidationSeverity.Blocking,
                    "Shoprite PO inbox"));
            }
            else if (matches.Count > 1)
            {
                issues.Add(new ValidationIssue(
                    "ambiguous-local-shoprite-po",
                    $"Shoprite PO {invoice.ShopritePurchaseOrderNumber} matched multiple local PO records.",
                    ValidationSeverity.Blocking,
                    "integration-config"));
            }
            else
            {
                var purchaseOrder = matches[0];
                matchedPurchaseOrderId = purchaseOrder.Id;
                invoice = invoice with
                {
                    SupplierGln = string.IsNullOrWhiteSpace(purchaseOrder.SupplierGln)
                        ? invoice.SupplierGln
                        : purchaseOrder.SupplierGln,
                    SellerVatRegistrationNumber = ShopriteSupplierProfile.EffectiveSellerVatRegistrationNumber(
                        invoice.SellerVatRegistrationNumber),
                    StoreDcGln = string.IsNullOrWhiteSpace(purchaseOrder.DeliveryGln)
                        ? invoice.StoreDcGln
                        : purchaseOrder.DeliveryGln,
                    Lines = EnrichLines(invoice.Lines, purchaseOrder.Lines)
                };
            }
        }

        var baseValidation = ShopriteInvoiceValidator.Validate(invoice, ShopriteValidationEnvironment.Qa);
        var validation = issues.Count == 0
            ? baseValidation
            : new ValidationResult(baseValidation.Issues.Concat(issues).ToArray());
        return new MatchedInvoiceValidation(invoice, matchedPurchaseOrderId, validation);
    }

    private static IReadOnlyList<CanonicalInvoiceLine> EnrichLines(
        IReadOnlyList<CanonicalInvoiceLine> invoiceLines,
        IReadOnlyCollection<ShopritePurchaseOrderLineEntity> purchaseOrderLines)
        => invoiceLines.Select(line =>
        {
            var matches = purchaseOrderLines
                .Where(orderLine => string.Equals(
                    orderLine.SupplierItemId,
                    line.AcumaticaInventoryId,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var match = matches.Length == 1 ? matches[0] : null;
            return match is null || !string.IsNullOrWhiteSpace(line.Gtin)
                ? line
                : line with { Gtin = match.Gtin };
        }).ToArray();

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

    private sealed record MatchedInvoiceValidation(
        CanonicalInvoice Invoice,
        Guid? MatchedPurchaseOrderId,
        ValidationResult Validation);
}

public sealed record AcumaticaInvoiceRefreshResult(int Received, int Created, int Updated);
