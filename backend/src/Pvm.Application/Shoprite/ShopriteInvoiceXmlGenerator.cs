using System.Globalization;
using System.Xml.Linq;
using Pvm.Domain.Invoices;

namespace Pvm.Application.Shoprite;

public static class ShopriteInvoiceXmlGenerator
{
    private static readonly XNamespace InvoiceNamespace = "urn:gs1:ecom:invoice:xsd:3";
    private static readonly XNamespace HeaderNamespace = "http://www.unece.org/cefact/namespaces/StandardBusinessDocumentHeader";
    private static readonly XNamespace XsdNamespace = "http://www.w3.org/2001/XMLSchema";
    private static readonly XNamespace XsiNamespace = "http://www.w3.org/2001/XMLSchema-instance";

    public static string Generate(CanonicalInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var createdAt = invoice.InvoiceDate.ToString("O", CultureInfo.InvariantCulture);
        var effectiveDate = invoice.InvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var supplierGln = invoice.SupplierGln ?? string.Empty;
        var storeDcGln = invoice.StoreDcGln ?? string.Empty;

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                InvoiceNamespace + "invoiceMessage",
                new XAttribute(XNamespace.Xmlns + "xsd", XsdNamespace),
                new XAttribute(XNamespace.Xmlns + "xsi", XsiNamespace),
                StandardBusinessDocumentHeader(invoice, createdAt, supplierGln, storeDcGln),
                Invoice(invoice, createdAt, effectiveDate, supplierGln, storeDcGln)));

        return document.Declaration + document.ToString(SaveOptions.DisableFormatting);
    }

    private static XElement StandardBusinessDocumentHeader(
        CanonicalInvoice invoice,
        string createdAt,
        string supplierGln,
        string storeDcGln)
        => new(
            HeaderNamespace + "StandardBusinessDocumentHeader",
            new XElement(HeaderNamespace + "HeaderVersion", "3.2.0"),
            new XElement(
                HeaderNamespace + "Sender",
                new XElement(
                    HeaderNamespace + "Identifier",
                    new XAttribute("Authority", "SenderEAN"),
                    supplierGln)),
            new XElement(
                HeaderNamespace + "Receiver",
                new XElement(
                    HeaderNamespace + "Identifier",
                    new XAttribute("Authority", "ReceiverEAN"),
                    storeDcGln)),
            new XElement(
                HeaderNamespace + "DocumentIdentification",
                new XElement(HeaderNamespace + "Standard", "Standard"),
                new XElement(HeaderNamespace + "TypeVersion", "3.2.0"),
                new XElement(HeaderNamespace + "InstanceIdentifier", invoice.InvoiceNumber),
                new XElement(HeaderNamespace + "Type", "Invoice"),
                new XElement(HeaderNamespace + "MultipleType", "false"),
                new XElement(HeaderNamespace + "CreationDateAndTime", createdAt)),
            new XElement(HeaderNamespace + "Manifest"));

    private static XElement Invoice(
        CanonicalInvoice invoice,
        string createdAt,
        string effectiveDate,
        string supplierGln,
        string storeDcGln)
        => new(
            "invoice",
            new XElement("creationDateTime", createdAt),
            new XElement("documentStatusCode", "ORIGINAL"),
            new XElement("documentActionCode", "ADD"),
            new XElement("documentStructureVersion", "3.2.0"),
            new XElement("revisionNumber", "1.0"),
            new XElement(
                "documentEffectiveDate",
                new XElement("date", effectiveDate)),
            new XElement(
                "InvoiceIdentification",
                new XElement("entityIdentification", invoice.InvoiceNumber),
                new XElement(
                    "contentOwner",
                    new XElement("gln", supplierGln))),
            new XElement("invoiceType", "INVOICE"),
            new XElement("invoiceCurrencyCode", invoice.CurrencyCode),
            new XElement("countryOfSupplyOfGoods", invoice.CountryCode),
            new XElement(
                "buyer",
                new XElement("gln", storeDcGln)),
            new XElement(
                "seller",
                new XElement("gln", supplierGln)),
            new XElement(
                "shipTo",
                new XElement("gln", storeDcGln)),
            new XElement(
                "invoiceTotals",
                MoneyElement("totalInvoiceAmount", invoice.TotalExcludingTax),
                MoneyElement("totalInvoiceAmountPayable", invoice.TotalIncludingTax),
                MoneyElement("totalVATAmount", invoice.TotalTax)),
            new XElement(
                "purchaseOrder",
                new XElement("entityIdentification", invoice.ShopritePurchaseOrderNumber ?? string.Empty)),
            invoice.Lines.Select(line => InvoiceLineItem(line, effectiveDate)));

    private static XElement InvoiceLineItem(CanonicalInvoiceLine line, string effectiveDate)
        => new(
            "invoiceLineItem",
            new XElement("lineItemNumber", line.LineNumber.ToString(CultureInfo.InvariantCulture)),
            new XElement("invoicedQuantity", Quantity(line.Quantity)),
            MoneyElement("amountExclusiveAllowancesCharges", line.UnitAmountExcludingTax),
            MoneyElement("amountInclusiveAllowancesCharges", line.UnitAmountIncludingTax),
            new XElement("transferOfOwnershipDate", effectiveDate),
            new XElement(
                "note",
                new XAttribute("languageCode", "EN"),
                line.Description),
            new XElement(
                "transactionalTradeItem",
                new XElement("gtin", line.Gtin ?? string.Empty),
                new XElement(
                    "transactionalItemData",
                    new XElement(
                        "transactionalItemWeight",
                        new XElement(
                            "measurementValue",
                            new XAttribute("measurementUnitCode", line.ShopriteUom?.ToString() ?? string.Empty),
                            "0"))),
                new XElement(
                    "size",
                    new XElement("sizeCode", Quantity(line.PackSize ?? 1m)))),
            new XElement(
                "invoiceLineTaxInformation",
                MoneyElement("dutyFeeTaxAmount", line.TaxAmount),
                new XElement("dutyFeeTaxCategoryCode", line.TaxCategoryCode ?? string.Empty),
                new XElement("dutyFeeTaxPercentage", Quantity(line.TaxPercentage ?? 0m)),
                new XElement("dutyFeeTaxTypeCode", "VAT")));

    private static XElement MoneyElement(string name, Money money)
        => new(
            name,
            new XAttribute("currencyCode", money.CurrencyCode),
            Amount(money.Amount));

    private static string Amount(decimal value)
        => value.ToString("0.0000", CultureInfo.InvariantCulture);

    private static string Quantity(decimal value)
        => value.ToString("0.####", CultureInfo.InvariantCulture);
}
