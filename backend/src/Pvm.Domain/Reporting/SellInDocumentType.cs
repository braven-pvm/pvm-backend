namespace Pvm.Domain.Reporting;

/// <summary>The Acumatica AR document types that count as sales in reports.</summary>
public enum SellInDocumentType
{
    Invoice,
    CreditMemo,
    DebitMemo
}

public static class SellInDocumentTypes
{
    /// <summary>
    /// Reads the Acumatica AR document type code (INV, CRM, DRM). Returns null for every other
    /// type, such as payments, which are not sales.
    /// </summary>
    public static SellInDocumentType? FromAcumaticaCode(string? code) => code?.Trim().ToUpperInvariant() switch
    {
        "INV" => SellInDocumentType.Invoice,
        "CRM" => SellInDocumentType.CreditMemo,
        "DRM" => SellInDocumentType.DebitMemo,
        _ => null
    };

    public static string ToAcumaticaCode(this SellInDocumentType type) => type switch
    {
        SellInDocumentType.Invoice => "INV",
        SellInDocumentType.CreditMemo => "CRM",
        SellInDocumentType.DebitMemo => "DRM",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    /// <summary>
    /// Acumatica keeps credit memo lines as positive values. A report shows them as negative
    /// sales, as the sales workbook does.
    /// </summary>
    public static decimal Signed(this SellInDocumentType type, decimal value) =>
        type == SellInDocumentType.CreditMemo ? -Math.Abs(value) : value;
}
