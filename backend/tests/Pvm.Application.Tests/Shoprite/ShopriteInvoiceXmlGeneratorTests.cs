using System.Xml.Linq;
using Pvm.Application.Shoprite;
using Pvm.Domain.Invoices;

namespace Pvm.Application.Tests.Shoprite;

public sealed class ShopriteInvoiceXmlGeneratorTests
{
    [Fact]
    public void Generate_includes_required_invoice_identity_fields()
    {
        var invoice = ValidInvoice();

        var xml = ShopriteInvoiceXmlGenerator.Generate(invoice);

        var document = XDocument.Parse(xml);
        var root = Assert.IsType<XElement>(document.Root);

        Assert.Equal("invoiceMessage", root.Name.LocalName);
        Assert.Equal("INV342699282", SingleValue(root, "InstanceIdentifier"));
        Assert.Equal("3869384391", SingleValue(root, "purchaseOrder", "entityIdentification"));
        Assert.Equal("16001069205048", SingleValue(root, "gtin"));
        Assert.Contains(root.Descendants(), element =>
            element.Name.LocalName == "measurementValue"
            && (string?)element.Attribute("measurementUnitCode") == "EA");
    }

    [Fact]
    public void Generate_escapes_text_values()
    {
        var invoice = ValidInvoice() with
        {
            InvoiceNumber = "INV<&>\"1",
            Lines =
            [
                ValidLine() with
                {
                    Description = "Item <one> & \"quoted\""
                }
            ]
        };

        var xml = ShopriteInvoiceXmlGenerator.Generate(invoice);

        Assert.Contains("INV&lt;&amp;&gt;\"1", xml);
        Assert.Contains("Item &lt;one&gt; &amp; \"quoted\"", xml);

        var document = XDocument.Parse(xml);
        var root = Assert.IsType<XElement>(document.Root);

        Assert.Equal("INV<&>\"1", SingleValue(root, "InstanceIdentifier"));
        Assert.Equal("Item <one> & \"quoted\"", SingleValue(root, "note"));
    }

    private static CanonicalInvoice ValidInvoice()
        => new(
            AcumaticaInvoiceId: "INV-1",
            InvoiceNumber: "INV342699282",
            CustomerAccount: "SHOPRITE",
            CustomerLocation: "DC-01",
            ShopritePurchaseOrderNumber: "3869384391",
            SupplierGln: "9999999999999",
            StoreDcGln: "6001001018104",
            CountryCode: "ZA",
            CurrencyCode: "ZAR",
            InvoiceDate: new DateTimeOffset(2026, 5, 18, 10, 0, 0, TimeSpan.Zero),
            TotalExcludingTax: new Money("ZAR", 109.8765m),
            TotalIncludingTax: new Money("ZAR", 125.1789m),
            TotalTax: new Money("ZAR", 15.3024m),
            Lines: [ValidLine()]);

    private static CanonicalInvoiceLine ValidLine()
        => new(
            LineNumber: 1,
            AcumaticaInventoryId: "SKU-1",
            Gtin: "16001069205048",
            Description: "Item 1",
            Quantity: 1m,
            AcumaticaUom: "EA",
            ShopriteUom: ShopriteMeasurementUnit.EA,
            PackSize: 24m,
            UnitAmountExcludingTax: new Money("ZAR", 109.8765m),
            UnitAmountIncludingTax: new Money("ZAR", 125.1789m),
            TaxAmount: new Money("ZAR", 15.3024m),
            TaxCategoryCode: "STANDARD",
            TaxPercentage: 15m,
            IsCatchWeight: false);

    private static string SingleValue(XElement root, params string[] localNamePath)
    {
        var matches = root
            .Descendants()
            .Where(element => element.Name.LocalName == localNamePath[^1])
            .Where(element => HasAncestorPath(element, localNamePath[..^1]))
            .Select(element => element.Value)
            .ToArray();

        var value = Assert.Single(matches);
        return value;
    }

    private static bool HasAncestorPath(XElement element, IReadOnlyList<string> localNamePath)
    {
        for (var i = localNamePath.Count - 1; i >= 0; i--)
        {
            element = element.Parent!;

            if (element.Name.LocalName != localNamePath[i])
            {
                return false;
            }
        }

        return true;
    }
}
