using Pvm.Application.Shoprite;

namespace Pvm.Application.Submissions;

public sealed class SubmitShopriteInvoiceHandler(
    IInvoiceCandidateRepository repository,
    IShopriteInvoiceClient shopriteClient)
{
    public async Task<SubmitShopriteInvoiceResult> HandleAsync(
        SubmitShopriteInvoiceCommand command,
        CancellationToken cancellationToken)
    {
        var invoice = await repository.GetCanonicalInvoiceAsync(command.InvoiceCandidateId, cancellationToken);
        if (invoice is null)
        {
            return new SubmitShopriteInvoiceResult(SubmitShopriteInvoiceStatus.Failed, "Invoice candidate not found.");
        }

        var validation = await repository.GetValidationResultAsync(command.InvoiceCandidateId, cancellationToken);
        if (!validation.CanSubmit)
        {
            return new SubmitShopriteInvoiceResult(SubmitShopriteInvoiceStatus.ValidationBlocked, "Invoice has blocking validation issues.");
        }

        if (await repository.HasSuccessfulSubmissionAsync(command.InvoiceCandidateId, cancellationToken))
        {
            return new SubmitShopriteInvoiceResult(SubmitShopriteInvoiceStatus.DuplicateBlocked, "Invoice already has a successful submission.");
        }

        var xml = ShopriteInvoiceXmlGenerator.Generate(invoice);
        var response = await shopriteClient.SubmitAsync(xml, cancellationToken);
        await repository.RecordAttemptAsync(
            command.InvoiceCandidateId,
            command.InitiatedBy,
            command.InitiationMode,
            xml,
            response,
            cancellationToken);

        if (response.IsAmbiguous)
        {
            return new SubmitShopriteInvoiceResult(SubmitShopriteInvoiceStatus.Ambiguous, "Submission outcome is ambiguous and requires manual review.");
        }

        return response.Success
            ? new SubmitShopriteInvoiceResult(SubmitShopriteInvoiceStatus.Submitted, "Invoice submitted to Shoprite.")
            : new SubmitShopriteInvoiceResult(SubmitShopriteInvoiceStatus.Failed, "Shoprite rejected or failed the submission.");
    }
}
