using System.Text.Json;

namespace Pvm.Application.Documents;

/// <summary>Proof of delivery: a signed paper that shows a client received goods on a PVM invoice.</summary>
public sealed class ProofOfDeliveryDocumentType : IDocumentType
{
    private const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "InvoiceNumbers": { "type": "array", "items": { "type": "string" } },
            "SalesOrderNumbers": { "type": "array", "items": { "type": "string" } },
            "CustomerOrderNumbers": { "type": "array", "items": { "type": "string" } },
            "ClientName": { "type": ["string", "null"] },
            "DeliveryDate": { "type": ["string", "null"], "format": "date" },
            "SignaturePresent": { "type": "boolean" },
            "Note": { "type": ["string", "null"] }
          },
          "required": [
            "InvoiceNumbers",
            "SalesOrderNumbers",
            "CustomerOrderNumbers",
            "ClientName",
            "DeliveryDate",
            "SignaturePresent",
            "Note"
          ]
        }
        """;

    private static readonly JsonElement ParsedSchema = JsonDocument.Parse(Schema).RootElement.Clone();

    public string Code => "POD";

    public string DisplayName => "Proof of delivery";

    public string AcumaticaEntity => "SalesInvoice";

    public JsonElement FieldSchema => ParsedSchema;

    public string ExtractionInstruction => """
        The attached PDF is a scanned proof of delivery for goods that PVM delivered to a client.
        It is one of these papers, sometimes several on one scan:
        - PVM's own invoice, signed and stamped by the client on receipt.
        - The client's delivery note or goods-received note (GRN).

        Read the fields below. Report every candidate you see for each number; do not choose one.
        - InvoiceNumbers: every PVM invoice number on the paper.
        - SalesOrderNumbers: every PVM sales-order number.
        - CustomerOrderNumbers: every client purchase-order (PO) or order number.
        - ClientName: the name of the client company or store that received the goods.
        - DeliveryDate: the date the goods were received, as YYYY-MM-DD.
        - SignaturePresent: true if a handwritten signature of the receiver is on the paper.
        - Note: anything unclear, such as a number you could not read with certainty.

        Copy numbers exactly as printed, without labels. A client's own document number
        (for example a GRN number) is not a PVM invoice number; leave it out of InvoiceNumbers.
        Use an empty list when there is no number of a kind, and null when a value is missing or unreadable.
        """;
}
