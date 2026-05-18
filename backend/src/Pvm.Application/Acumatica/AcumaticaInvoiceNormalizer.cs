using Pvm.Domain.Invoices;

namespace Pvm.Application.Acumatica;

public static class AcumaticaInvoiceNormalizer
{
    public static CanonicalInvoice Normalize(
        AcumaticaInvoiceDto dto,
        string? supplierGln,
        string? storeDcGln)
    {
        return new CanonicalInvoice(
            AcumaticaInvoiceId: dto.Id,
            InvoiceNumber: dto.InvoiceNumber,
            CustomerAccount: dto.CustomerAccount,
            CustomerLocation: dto.CustomerLocation,
            ShopritePurchaseOrderNumber: dto.CustomerOrder,
            SupplierGln: supplierGln,
            StoreDcGln: storeDcGln,
            CountryCode: dto.CountryCode,
            CurrencyCode: dto.CurrencyCode,
            InvoiceDate: dto.InvoiceDate,
            TotalExcludingTax: new Money(dto.CurrencyCode, dto.TotalExcludingTax),
            TotalIncludingTax: new Money(dto.CurrencyCode, dto.TotalIncludingTax),
            TotalTax: new Money(dto.CurrencyCode, dto.TotalTax),
            Lines: dto.Lines.Select(line => new CanonicalInvoiceLine(
                LineNumber: line.LineNumber,
                AcumaticaInventoryId: line.InventoryId,
                Gtin: line.Gtin,
                Description: line.Description,
                Quantity: line.Quantity,
                AcumaticaUom: line.Uom,
                ShopriteUom: MapUom(line.Uom),
                PackSize: line.PackSize,
                UnitAmountExcludingTax: new Money(dto.CurrencyCode, line.UnitAmountExcludingTax),
                UnitAmountIncludingTax: new Money(dto.CurrencyCode, line.UnitAmountIncludingTax),
                TaxAmount: new Money(dto.CurrencyCode, line.TaxAmount),
                TaxCategoryCode: line.TaxCategoryCode,
                TaxPercentage: line.TaxPercentage,
                IsCatchWeight: line.IsCatchWeight)).ToList());
    }

    private static ShopriteMeasurementUnit? MapUom(string uom)
    {
        return Enum.TryParse<ShopriteMeasurementUnit>(uom, ignoreCase: true, out var mapped)
            ? mapped
            : null;
    }
}
