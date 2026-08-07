namespace Pvm.Application.Submissions;

public sealed record InvoiceSourceVersionVerification(bool IsCurrent, string Message);

public interface IInvoiceSourceVersionVerifier
{
    Task<InvoiceSourceVersionVerification> VerifyAsync(
        string? sourceJson,
        CancellationToken cancellationToken);
}
