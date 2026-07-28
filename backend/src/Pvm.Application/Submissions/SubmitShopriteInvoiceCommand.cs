namespace Pvm.Application.Submissions;

public sealed record SubmitShopriteInvoiceCommand(
    Guid CommandId,
    Guid InvoiceCandidateId,
    string InitiatedBy,
    string InitiationMode);
