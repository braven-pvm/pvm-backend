namespace Pvm.Application.Submissions;

public enum SubmitShopriteInvoiceStatus
{
    Submitted,
    ValidationBlocked,
    DuplicateBlocked,
    Ambiguous,
    Failed
}

public sealed record SubmitShopriteInvoiceResult(
    SubmitShopriteInvoiceStatus Status,
    string Message);
