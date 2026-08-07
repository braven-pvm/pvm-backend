using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pvm.Infrastructure.Acumatica;

public sealed record AcumaticaPushNotification(
    [property: JsonPropertyName("Inserted")] JsonElement[] Inserted,
    [property: JsonPropertyName("Deleted")] JsonElement[] Deleted,
    [property: JsonPropertyName("Query")] string Query,
    [property: JsonPropertyName("CompanyId")] string CompanyId,
    [property: JsonPropertyName("Id")] Guid Id,
    [property: JsonPropertyName("TimeStamp")] long TimeStamp,
    [property: JsonPropertyName("AdditionalInfo")] JsonElement? AdditionalInfo)
{
    public IReadOnlyList<AcumaticaPushInvoiceReference> InvoiceReferences()
    {
        var references = new Dictionary<string, AcumaticaPushInvoiceReference>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var row in Inserted.Concat(Deleted))
        {
            var invoiceId = String(row, "InvoiceId", "NoteID", "NoteId", "id");
            if (string.IsNullOrWhiteSpace(invoiceId))
            {
                continue;
            }

            references[invoiceId] = new AcumaticaPushInvoiceReference(
                invoiceId,
                String(row, "ReferenceNbr", "ReferenceNumber", "RefNbr"));
        }

        return references.Values.ToArray();
    }

    private static string? String(JsonElement row, params string[] names)
    {
        if (row.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in row.EnumerateObject())
        {
            if (!names.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            return property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetRawText(),
                _ => null
            };
        }

        return null;
    }
}

public sealed record AcumaticaPushInvoiceReference(string InvoiceId, string? ReferenceNumber);
