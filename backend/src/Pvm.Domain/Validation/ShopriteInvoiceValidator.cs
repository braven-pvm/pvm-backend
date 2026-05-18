using Pvm.Domain.Invoices;

namespace Pvm.Domain.Validation;

public enum ShopriteValidationEnvironment
{
    Qa,
    Production
}

public static class ShopriteInvoiceValidator
{
    public static ValidationResult Validate(CanonicalInvoice invoice, ShopriteValidationEnvironment environment)
    {
        var issues = new List<ValidationIssue>();

        Require(invoice.ShopritePurchaseOrderNumber, "missing-shoprite-po", "Shoprite PO number is required.", "Acumatica", issues);
        Require(invoice.SupplierGln, "missing-supplier-gln", "Supplier GLN is required.", "integration-config", issues);
        Require(invoice.StoreDcGln, "missing-store-dc-gln", "Store/DC GLN is required.", "integration-config", issues);

        if (!StringEquals(invoice.CountryCode, "ZA"))
        {
            issues.Add(Block("unsupported-country", "MVP supports South Africa only.", "Acumatica"));
        }

        if (!StringEquals(invoice.CurrencyCode, "ZAR"))
        {
            issues.Add(Block("unsupported-currency", "MVP supports ZAR only.", "Acumatica"));
        }

        foreach (var line in invoice.Lines)
        {
            if (line.Quantity <= 0)
            {
                issues.Add(Block("zero-quantity-line", $"Line {line.LineNumber} has zero or negative quantity.", "Acumatica"));
            }

            Require(line.Gtin, "missing-gtin", $"Line {line.LineNumber} is missing GTIN.", "integration-config", issues);

            if (line.ShopriteUom is null)
            {
                var severity = environment == ShopriteValidationEnvironment.Production
                    ? ValidationSeverity.Blocking
                    : ValidationSeverity.Warning;

                issues.Add(new ValidationIssue(
                    "unverified-shoprite-uom",
                    $"Line {line.LineNumber} has unresolved Shoprite UOM mapping.",
                    severity,
                    "integration-config"));
            }
            else if (!Enum.IsDefined(line.ShopriteUom.Value))
            {
                issues.Add(Block("unsupported-shoprite-uom", $"Line {line.LineNumber} has unsupported Shoprite UOM.", "integration-config"));
            }

            if (line.IsCatchWeight)
            {
                issues.Add(Block("catch-weight-unsupported", $"Line {line.LineNumber} is catch weight and is excluded from MVP.", "Acumatica"));
            }
        }

        var lineExcluding = invoice.Lines.Sum(line => RoundCurrency(line.UnitAmountExcludingTax.Amount * line.Quantity));
        var lineIncluding = invoice.Lines.Sum(line => RoundCurrency(line.UnitAmountIncludingTax.Amount * line.Quantity));
        var lineTax = invoice.Lines.Sum(line => RoundCurrency(line.TaxAmount.Amount * line.Quantity));

        if (lineExcluding != RoundCurrency(invoice.TotalExcludingTax.Amount) ||
            lineIncluding != RoundCurrency(invoice.TotalIncludingTax.Amount) ||
            lineTax != RoundCurrency(invoice.TotalTax.Amount))
        {
            issues.Add(Block("totals-mismatch", "Calculated line totals do not match invoice totals.", "Acumatica"));
        }

        return new ValidationResult(issues);
    }

    private static void Require(string? value, string code, string message, string fixLocation, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(Block(code, message, fixLocation));
        }
    }

    private static decimal RoundCurrency(decimal value)
        => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static bool StringEquals(string value, string expected)
        => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private static ValidationIssue Block(string code, string message, string fixLocation)
        => new(code, message, ValidationSeverity.Blocking, fixLocation);
}
