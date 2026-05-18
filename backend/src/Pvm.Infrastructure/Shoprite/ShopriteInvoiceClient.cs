using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using Pvm.Application.Submissions;

namespace Pvm.Infrastructure.Shoprite;

public sealed class ShopriteInvoiceClient(
    HttpClient httpClient,
    IOptions<ShopriteOptions> options) : IShopriteInvoiceClient
{
    private readonly ShopriteOptions _options = options.Value;

    public async Task<ShopriteInvoiceResponse> SubmitAsync(
        string xml,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "VendorInvoice");
        request.Content = new StringContent(xml, Encoding.UTF8, "application/xml");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}")));
        request.Headers.Add("ContractID", _options.ContractId);
        request.Headers.Add("UIUser", _options.UiUser);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new ShopriteInvoiceResponse(
                response.IsSuccessStatusCode,
                (int)response.StatusCode,
                body,
                IsAmbiguous: false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ShopriteInvoiceResponse(
                false,
                null,
                "Shoprite request timed out.",
                IsAmbiguous: true);
        }
        catch (HttpRequestException exception)
        {
            return new ShopriteInvoiceResponse(
                false,
                null,
                exception.Message,
                IsAmbiguous: true);
        }
    }
}
