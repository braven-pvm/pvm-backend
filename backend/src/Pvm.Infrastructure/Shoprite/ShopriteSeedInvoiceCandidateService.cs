using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pvm.Application.Shoprite;
using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Shoprite;

public sealed class ShopriteSeedInvoiceCandidateService(PvmDbContext dbContext)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<InvoiceCandidateEntity?> SeedAsync(
        Guid purchaseOrderId,
        DateTimeOffset invoiceDate,
        CancellationToken cancellationToken)
    {
        var order = await dbContext.ShopritePurchaseOrders
            .Include(order => order.Lines)
            .SingleOrDefaultAsync(order => order.Id == purchaseOrderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var canonical = ShopriteSeedInvoiceFactory.FromPurchaseOrder(ToSeedPurchaseOrder(order), invoiceDate);
        var validation = ShopriteInvoiceValidator.Validate(canonical, ShopriteValidationEnvironment.Qa);
        var idempotencyKey = BuildIdempotencyKey(canonical);
        var now = DateTimeOffset.UtcNow;

        var candidate = await dbContext.InvoiceCandidates
            .SingleOrDefaultAsync(
                candidate => candidate.AcumaticaInvoiceId == canonical.AcumaticaInvoiceId,
                cancellationToken);

        if (candidate is null)
        {
            candidate = new InvoiceCandidateEntity
            {
                Id = Guid.NewGuid(),
                AcumaticaInvoiceId = canonical.AcumaticaInvoiceId,
                InvoiceNumber = canonical.InvoiceNumber,
                CustomerAccount = canonical.CustomerAccount,
                IdempotencyKey = idempotencyKey,
                Status = CandidateStatus(validation, currentStatus: null),
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.InvoiceCandidates.Add(candidate);
        }

        candidate.AcumaticaInvoiceId = canonical.AcumaticaInvoiceId;
        candidate.InvoiceNumber = canonical.InvoiceNumber;
        candidate.CustomerAccount = canonical.CustomerAccount;
        candidate.CustomerLocation = canonical.CustomerLocation;
        candidate.ShopritePurchaseOrderNumber = canonical.ShopritePurchaseOrderNumber;
        candidate.MatchedShopritePurchaseOrderId = order.Id;
        candidate.SupplierGln = canonical.SupplierGln;
        candidate.StoreDcGln = canonical.StoreDcGln;
        candidate.IdempotencyKey = idempotencyKey;
        candidate.Status = CandidateStatus(validation, candidate.Status);
        candidate.SourceJson = order.RawOrderJson;
        candidate.CanonicalJson = JsonSerializer.Serialize(canonical, SerializerOptions);
        candidate.ValidationJson = JsonSerializer.Serialize(validation, SerializerOptions);
        candidate.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);

        return candidate;
    }

    private static SeedShopritePurchaseOrder ToSeedPurchaseOrder(ShopritePurchaseOrderEntity order)
        => new(
            order.PurchaseOrderNumber,
            order.SupplierGln,
            order.DeliveryGln,
            order.DeliveryLocationCode,
            order.DeliveryLocationName,
            order.CurrencyCode,
            order.Lines.Select(line => new SeedShopritePurchaseOrderLine(
                    line.LineNumber,
                    line.Gtin,
                    line.BuyerItemId,
                    line.Description,
                    line.RequestedQuantity,
                    line.MeasurementUnitCode,
                    line.NetPrice,
                    line.MonetaryAmountExcludingTaxes,
                    line.MonetaryAmountIncludingTaxes,
                    line.SupplierItemId,
                    line.BuyerItemDescription))
                .ToArray());

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
