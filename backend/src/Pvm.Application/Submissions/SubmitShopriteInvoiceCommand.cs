namespace Pvm.Application.Submissions;

public sealed record SubmitShopriteInvoiceCommand(
    Guid InvoiceCandidateId,
    string InitiatedBy,
    string InitiationMode);
