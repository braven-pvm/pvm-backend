namespace Pvm.Application.Submissions;

public enum SubmitShopriteInvoiceStatus
{
    Submitted,
    InProgress,
    ValidationBlocked,
    DuplicateBlocked,
    ManualReviewRequired,
    Ambiguous,
    Failed
}

public sealed record SubmitShopriteInvoiceResult(
    SubmitShopriteInvoiceStatus Status,
    string Message,
    Guid? SubmissionOperationId = null);
