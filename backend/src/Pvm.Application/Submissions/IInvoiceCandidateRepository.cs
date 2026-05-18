using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;

namespace Pvm.Application.Submissions;

public interface IInvoiceCandidateRepository
{
    Task<CanonicalInvoice?> GetCanonicalInvoiceAsync(Guid invoiceCandidateId, CancellationToken cancellationToken);

    Task<ValidationResult> GetValidationResultAsync(Guid invoiceCandidateId, CancellationToken cancellationToken);

    Task<bool> HasSuccessfulSubmissionAsync(Guid invoiceCandidateId, CancellationToken cancellationToken);

    Task RecordAttemptAsync(
        Guid invoiceCandidateId,
        string initiatedBy,
        string initiationMode,
        string xml,
        ShopriteInvoiceResponse response,
        CancellationToken cancellationToken);
}
