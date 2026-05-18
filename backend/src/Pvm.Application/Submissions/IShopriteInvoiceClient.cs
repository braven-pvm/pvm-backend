namespace Pvm.Application.Submissions;

public interface IShopriteInvoiceClient
{
    /// <summary>
    /// Returns a response for known Shoprite outcomes. Transport timeouts or network failures
    /// after submission uncertainty must be represented as IsAmbiguous instead of escaping.
    /// </summary>
    Task<ShopriteInvoiceResponse> SubmitAsync(string xml, CancellationToken cancellationToken);
}

public sealed record ShopriteInvoiceResponse(
    bool Success,
    int? StatusCode,
    string Body,
    bool IsAmbiguous);
