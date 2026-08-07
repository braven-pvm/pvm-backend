using Microsoft.Extensions.Primitives;
using Pvm.Api.Features.AcumaticaPushNotifications;
using Pvm.Infrastructure.Acumatica;

namespace Pvm.Api.Tests.AcumaticaPushNotifications;

public sealed class AcumaticaPushNotificationContractTests
{
    [Fact]
    public void Parse_ValidNotification_DeduplicatesInvoiceReferencesAcrossRowSets()
    {
        var notification = AcumaticaPushNotificationParser.Parse("""
            {
              "Inserted": [
                { "InvoiceId": "c340d968-71a3-4ced-8c91-7cf1e653bec4", "ReferenceNbr": "INV000123" }
              ],
              "Deleted": [
                { "NoteID": "c340d968-71a3-4ced-8c91-7cf1e653bec4", "ReferenceNbr": "INV000123" }
              ],
              "Query": "PVM-Shoprite-Finalized-Invoices",
              "CompanyId": "PVM",
              "Id": "1af4d140-5321-41f2-a2ec-50b67f577c6c",
              "TimeStamp": 639269280000000000,
              "AdditionalInfo": {}
            }
            """);

        var reference = Assert.Single(notification.InvoiceReferences());
        Assert.Equal("c340d968-71a3-4ced-8c91-7cf1e653bec4", reference.InvoiceId);
        Assert.Equal("INV000123", reference.ReferenceNumber);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not-json")]
    [InlineData("{\"Id\":\"00000000-0000-0000-0000-000000000000\",\"TimeStamp\":1,\"Query\":\"GI\",\"CompanyId\":\"PVM\"}")]
    public void Parse_InvalidNotification_IsRejected(string payload)
    {
        Assert.Throws<AcumaticaPushNotificationException>(
            () => AcumaticaPushNotificationParser.Parse(payload));
    }

    [Fact]
    public void SecretValidator_RequiresExactlyOneMatchingValue()
    {
        const string expected = "a-valid-webhook-secret-that-is-long-enough";

        Assert.True(AcumaticaPushNotificationSecretValidator.IsValid(
            new StringValues(expected), expected));
        Assert.False(AcumaticaPushNotificationSecretValidator.IsValid(
            new StringValues("wrong-secret"), expected));
        Assert.False(AcumaticaPushNotificationSecretValidator.IsValid(
            new StringValues([expected, expected]), expected));
    }
}
