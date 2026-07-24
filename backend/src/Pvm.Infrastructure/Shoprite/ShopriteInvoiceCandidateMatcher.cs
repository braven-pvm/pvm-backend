using Microsoft.EntityFrameworkCore;
using Pvm.Application.Shoprite;
using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Shoprite;

public sealed class ShopriteInvoiceCandidateMatcher(PvmDbContext dbContext)
{
    public async Task<ShopriteInvoiceMatchResult> MatchAndValidateAsync(
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
                    Lines = await EnrichLinesAsync(invoice.Lines, purchaseOrder.Lines, cancellationToken)
                };
            }
        }

        var baseValidation = ShopriteInvoiceValidator.Validate(invoice, ShopriteValidationEnvironment.Qa);
        var validation = issues.Count == 0
            ? baseValidation
            : new ValidationResult(baseValidation.Issues.Concat(issues).ToArray());

        return new ShopriteInvoiceMatchResult(invoice, matchedPurchaseOrderId, validation);
    }

    private async Task<IReadOnlyList<CanonicalInvoiceLine>> EnrichLinesAsync(
        IReadOnlyList<CanonicalInvoiceLine> invoiceLines,
        IReadOnlyCollection<ShopritePurchaseOrderLineEntity> purchaseOrderLines,
        CancellationToken cancellationToken)
    {
        var inventoryIds = invoiceLines
            .Select(line => Normalize(line.AcumaticaInventoryId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var itemMappings = await dbContext.ShopriteItemMappings
            .AsNoTracking()
            .Where(mapping => inventoryIds.Contains(mapping.AcumaticaInventoryId))
            .ToListAsync(cancellationToken);
        var uomMappings = await dbContext.ShopriteUomMappings
            .AsNoTracking()
            .Where(mapping => inventoryIds.Contains(mapping.AcumaticaInventoryId))
            .ToListAsync(cancellationToken);

        return invoiceLines.Select(line =>
        {
            var inventoryId = Normalize(line.AcumaticaInventoryId);
            var matchingItemMappings = itemMappings
                .Where(mapping =>
                    mapping.AcumaticaInventoryId == inventoryId
                    && mapping.IsVerified
                    && purchaseOrderLines.Any(orderLine =>
                        string.Equals(
                            orderLine.BuyerItemId,
                            mapping.ShopriteBuyerItemId,
                            StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            var mappedItem = matchingItemMappings.Length == 1
                ? matchingItemMappings[0]
                : null;
            var supplierItemMatches = purchaseOrderLines
                .Where(orderLine => string.Equals(
                    orderLine.SupplierItemId,
                    line.AcumaticaInventoryId,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var supplierItemMatch = supplierItemMatches.Length == 1
                ? supplierItemMatches[0]
                : null;
            var uom = Normalize(line.AcumaticaUom);
            var mappedUom = uomMappings.SingleOrDefault(mapping =>
                mapping.AcumaticaInventoryId == inventoryId
                && mapping.AcumaticaUom == uom);

            return line with
            {
                Gtin = !string.IsNullOrWhiteSpace(line.Gtin)
                    ? line.Gtin
                    : mappedItem?.Gtin ?? supplierItemMatch?.Gtin,
                ShopriteUom = mappedUom?.ShopriteUom ?? line.ShopriteUom,
                IsShopriteUomVerified = mappedUom?.IsVerified
                    ?? line.IsShopriteUomVerified
            };
        }).ToArray();
    }

    public static string Normalize(string value)
        => value.Trim().ToUpperInvariant();
}

public sealed record ShopriteInvoiceMatchResult(
    CanonicalInvoice Invoice,
    Guid? MatchedPurchaseOrderId,
    ValidationResult Validation);
