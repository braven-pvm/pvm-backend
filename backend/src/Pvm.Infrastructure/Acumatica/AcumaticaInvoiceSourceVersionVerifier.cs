using System.Text.Json;
using Pvm.Application.Acumatica;
using Pvm.Application.Submissions;

namespace Pvm.Infrastructure.Acumatica;

public sealed class AcumaticaInvoiceSourceVersionVerifier(
    IAcumaticaInvoiceClient invoiceClient) : IInvoiceSourceVersionVerifier
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<InvoiceSourceVersionVerification> VerifyAsync(
        string? sourceJson,
        CancellationToken cancellationToken)
    {
        AcumaticaInvoiceDto? prepared;
        try
        {
            prepared = string.IsNullOrWhiteSpace(sourceJson)
                ? null
                : JsonSerializer.Deserialize<AcumaticaInvoiceDto>(sourceJson, SerializerOptions);
        }
        catch (JsonException)
        {
            prepared = null;
        }

        if (prepared is null || string.IsNullOrWhiteSpace(prepared.Id) || prepared.LastModifiedAt is null)
        {
            return new InvoiceSourceVersionVerification(
                false,
                "The prepared invoice has no verifiable Acumatica source version.");
        }

        var current = await invoiceClient.FetchFinalizedInvoiceAsync(prepared.Id, cancellationToken);
        if (current?.LastModifiedAt is null)
        {
            return new InvoiceSourceVersionVerification(
                false,
                "The current finalized Acumatica invoice or its source version could not be verified.");
        }

        return current.LastModifiedAt == prepared.LastModifiedAt
            ? new InvoiceSourceVersionVerification(true, "Acumatica source version is current.")
            : new InvoiceSourceVersionVerification(
                false,
                "The Acumatica invoice changed after preparation and must be reconciled again.");
    }
}
