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
        if (string.IsNullOrWhiteSpace(_options.Username) ||
            string.IsNullOrWhiteSpace(_options.Password))
        {
            throw new InvalidOperationException("Shoprite invoice client credentials are not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildVendorInvoiceUri());
        request.Content = new StringContent(xml, Encoding.UTF8, "application/xml");
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

    private Uri BuildVendorInvoiceUri()
    {
        var userName = Uri.EscapeDataString(_options.Username!);
        var password = Uri.EscapeDataString(_options.Password!);
        return new Uri($"VendorInvoice?userName={userName}&password={password}", UriKind.Relative);
    }
}
