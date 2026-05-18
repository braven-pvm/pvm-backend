namespace Pvm.Application.Submissions;

public interface IShopriteInvoiceClient
{
    Task<ShopriteInvoiceResponse> SubmitAsync(string xml, CancellationToken cancellationToken);
}

public sealed record ShopriteInvoiceResponse(
    bool Success,
    int? StatusCode,
    string Body,
    bool IsAmbiguous);
