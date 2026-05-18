using Pvm.Application.Submissions;

namespace Pvm.Api.Features.Submissions;

public sealed class LocalShopriteInvoiceClient : IShopriteInvoiceClient
{
    public Task<ShopriteInvoiceResponse> SubmitAsync(string xml, CancellationToken cancellationToken)
        => Task.FromResult(new ShopriteInvoiceResponse(
            Success: true,
            StatusCode: StatusCodes.Status202Accepted,
            Body: "<VendorInvoiceResponse><Status>Accepted</Status></VendorInvoiceResponse>",
            IsAmbiguous: false));
}
