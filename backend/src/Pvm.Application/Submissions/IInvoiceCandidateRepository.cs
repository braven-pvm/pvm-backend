using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;

namespace Pvm.Application.Submissions;

public interface IInvoiceCandidateRepository
{
    Task<CanonicalInvoice?> GetCanonicalInvoiceAsync(Guid invoiceCandidateId, CancellationToken cancellationToken);

    Task<ValidationResult> GetValidationResultAsync(Guid invoiceCandidateId, CancellationToken cancellationToken);

    Task<bool> HasUnresolvedAmbiguousSubmissionAsync(Guid invoiceCandidateId, CancellationToken cancellationToken);

    // Concrete implementations must enforce successful-submission idempotency atomically
    // at the persistence boundary before this contract is wired to API/UI submission paths.
    Task<bool> HasSuccessfulSubmissionAsync(Guid invoiceCandidateId, CancellationToken cancellationToken);

    Task RecordAttemptAsync(
        Guid invoiceCandidateId,
        string initiatedBy,
        string initiationMode,
        string xml,
        ShopriteInvoiceResponse response,
        CancellationToken cancellationToken);
}
