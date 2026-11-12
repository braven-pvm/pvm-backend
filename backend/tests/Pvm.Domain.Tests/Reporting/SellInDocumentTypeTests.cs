using Pvm.Domain.Reporting;

namespace Pvm.Domain.Tests.Reporting;

public sealed class SellInDocumentTypeTests
{
    [Theory]
    [InlineData("INV", SellInDocumentType.Invoice)]
    [InlineData("crm", SellInDocumentType.CreditMemo)]
    [InlineData(" DRM ", SellInDocumentType.DebitMemo)]
    public void Sales_document_codes_are_read(string code, SellInDocumentType expected)
    {
        Assert.Equal(expected, SellInDocumentTypes.FromAcumaticaCode(code));
        Assert.Equal(code.Trim().ToUpperInvariant(), expected.ToAcumaticaCode());
    }

    [Theory]
    [InlineData("PMT")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_document_codes_are_not_sales(string? code)
    {
        Assert.Null(SellInDocumentTypes.FromAcumaticaCode(code));
    }

    [Theory]
    [InlineData(SellInDocumentType.Invoice, 120.50, 120.50)]
    [InlineData(SellInDocumentType.DebitMemo, 10, 10)]
    [InlineData(SellInDocumentType.CreditMemo, 120.50, -120.50)]
    [InlineData(SellInDocumentType.CreditMemo, -120.50, -120.50)]
    public void Credit_memos_count_as_negative_sales(SellInDocumentType type, decimal value, decimal expected)
    {
        Assert.Equal(expected, type.Signed(value));
    }
}
