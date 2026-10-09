using System.Globalization;
using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;

namespace Pvm.Application.Shoprite;

/// <summary>Whether Shoprite delivers to a store or to a distribution centre.</summary>
public enum ShopriteLocationType
{
    Store,
    DistributionCentre
}

/// <summary>
/// A Shoprite store or distribution centre that PVM delivers to. <see cref="BranchCode"/> is the
/// Shoprite branch number, which is also the Acumatica customer ID of that store.
/// </summary>
public sealed record ShopriteDeliveryLocation(
    string BranchCode,
    string Gln,
    string Name,
    ShopriteLocationType LocationType,
    bool IsVerified);

/// <summary>
/// How Shoprite orders one Acumatica item. Stores order single boxes, distribution centres order
/// cases with a different GTIN, so one item and unit can have one row per delivery type.
/// <see cref="AcumaticaUnitsPerShopriteUnit"/> is how many Acumatica units make one Shoprite unit,
/// for example 12 boxes to a case.
/// </summary>
public sealed record ShopriteTradeItem(
    string AcumaticaInventoryId,
    string AcumaticaUom,
    ShopriteLocationType DeliversTo,
    string Gtin,
    int AcumaticaUnitsPerShopriteUnit,
    decimal ShopritePackSize,
    ShopriteMeasurementUnit ShopriteUom,
    bool IsVerified);

public sealed record ShopriteReferenceEnrichment(
    CanonicalInvoice Invoice,
    IReadOnlyList<ValidationIssue> Issues);

/// <summary>
/// Completes a Shoprite invoice from reference data instead of from the Shoprite order.
///
/// Shoprite marks an order as downloaded the moment anything reads it, and there is no read that
/// avoids it. PVM's people work orders on the Shoprite portal, so the integration never reads
/// orders. What the invoice needs and Acumatica does not hold is slow-changing reference data:
/// the GLN of the store or distribution centre, and the GTIN Shoprite orders each item under.
/// </summary>
public static class ShopriteInvoiceReferenceEnricher
{
    private const string ReferenceData = "Shoprite reference data";

    public static ShopriteReferenceEnrichment Enrich(
        CanonicalInvoice invoice,
        ShopriteDeliveryLocation? location,
        IReadOnlyCollection<ShopriteTradeItem> tradeItems,
        IReadOnlyCollection<string> invoicesAlreadySubmittedForPurchaseOrder)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(tradeItems);
        ArgumentNullException.ThrowIfNull(invoicesAlreadySubmittedForPurchaseOrder);

        var issues = new List<ValidationIssue>();
        invoice = invoice with
        {
            SupplierGln = ShopriteSupplierProfile.PvmSupplierGln,
            SellerVatRegistrationNumber = ShopriteSupplierProfile.EffectiveSellerVatRegistrationNumber(
                invoice.SellerVatRegistrationNumber)
        };

        // Shoprite has no duplicate protection, so a second invoice for one PO would be paid twice.
        if (invoicesAlreadySubmittedForPurchaseOrder.Count > 0)
        {
            issues.Add(Block(
                "shoprite-po-already-invoiced",
                $"Shoprite PO {invoice.ShopritePurchaseOrderNumber} already has invoice "
                + $"{string.Join(", ", invoicesAlreadySubmittedForPurchaseOrder)} submitted. "
                + "Confirm this is a separate delivery before you submit it.",
                "Invoice"));
        }

        if (location is null)
        {
            issues.Add(Block(
                "missing-shoprite-delivery-location",
                $"Acumatica customer {Normalize(invoice.CustomerAccount)} has no Shoprite delivery location. "
                + "Add its GLN to the Shoprite reference data before you submit.",
                ReferenceData));
            return new ShopriteReferenceEnrichment(invoice, issues);
        }

        if (!location.IsVerified)
        {
            issues.Add(Block(
                "unverified-shoprite-delivery-location",
                $"The GLN for {location.Name} ({location.BranchCode}) is not verified yet.",
                ReferenceData));
        }

        var lines = invoice.Lines
            .Select(line => EnrichLine(line, location.LocationType, tradeItems, issues))
            .ToArray();

        return new ShopriteReferenceEnrichment(
            invoice with { StoreDcGln = location.Gln, Lines = lines },
            issues);
    }

    private static CanonicalInvoiceLine EnrichLine(
        CanonicalInvoiceLine line,
        ShopriteLocationType deliversTo,
        IReadOnlyCollection<ShopriteTradeItem> tradeItems,
        List<ValidationIssue> issues)
    {
        var inventoryId = Normalize(line.AcumaticaInventoryId);
        var uom = Normalize(line.AcumaticaUom);
        var item = tradeItems.FirstOrDefault(candidate =>
            Normalize(candidate.AcumaticaInventoryId) == inventoryId
            && Normalize(candidate.AcumaticaUom) == uom
            && candidate.DeliversTo == deliversTo);

        if (item is null)
        {
            issues.Add(Block(
                "missing-shoprite-trade-item",
                $"Line {line.LineNumber}: item {inventoryId} in {uom} has no Shoprite GTIN for a "
                + $"{Describe(deliversTo)} delivery.",
                ReferenceData));
            return line;
        }

        if (!item.IsVerified)
        {
            issues.Add(Block(
                "unverified-shoprite-trade-item",
                $"Line {line.LineNumber}: the Shoprite GTIN {item.Gtin} for item {inventoryId} "
                + $"({Describe(deliversTo)}) is not verified yet.",
                ReferenceData));
        }

        var factor = Math.Max(1, item.AcumaticaUnitsPerShopriteUnit);
        if (line.Quantity % factor != 0)
        {
            issues.Add(Block(
                "quantity-not-whole-shoprite-units",
                $"Line {line.LineNumber}: {Format(line.Quantity)} {uom} is not a whole number of Shoprite "
                + $"units of {factor} {uom}. Correct the quantity in Acumatica.",
                "Acumatica"));
            return line with { Gtin = item.Gtin };
        }

        // Acumatica amounts are per Acumatica unit. Converting the quantity and the unit amounts by
        // the same factor keeps every line total, and so the invoice totals, unchanged.
        return line with
        {
            Gtin = item.Gtin,
            Quantity = line.Quantity / factor,
            UnitAmountExcludingTax = Scale(line.UnitAmountExcludingTax, factor),
            UnitAmountIncludingTax = Scale(line.UnitAmountIncludingTax, factor),
            TaxAmount = Scale(line.TaxAmount, factor),
            ShopriteUom = item.ShopriteUom,
            PackSize = item.ShopritePackSize,
            IsShopriteUomVerified = item.IsVerified
        };
    }

    public static string Normalize(string? value)
        => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static Money Scale(Money money, int factor)
        => money with { Amount = money.Amount * factor };

    private static string Describe(ShopriteLocationType type)
        => type == ShopriteLocationType.DistributionCentre ? "distribution centre" : "store";

    private static string Format(decimal value)
        => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static ValidationIssue Block(string code, string message, string fixLocation)
        => new(code, message, ValidationSeverity.Blocking, fixLocation);
}
