using Pvm.Domain.Validation;

namespace Pvm.Api.Features.Invoices;

public static class InvoiceCandidateReadiness
{
    public static bool CanSubmit(
        ValidationResult validation,
        Guid? matchedPurchaseOrderId,
        string candidateStatus,
        IReadOnlyCollection<string> attemptStatuses)
        => validation.CanSubmit
            && matchedPurchaseOrderId is not null
            && candidateStatus is not "Submitted" and not "Ambiguous"
            && !attemptStatuses.Any(status => status is "Submitted" or "Ambiguous");
}
