using System.Globalization;
using Microsoft.Extensions.Options;
using Pvm.Application.Shoprite;

namespace Pvm.Infrastructure.Shoprite;

public sealed class ShopritePurchaseOrderClient(
    HttpClient httpClient,
    IOptions<ShopriteOptions> options) : IShopritePurchaseOrderClient
{
    private readonly ShopriteOptions _options = options.Value;

    public async Task<ShopritePurchaseOrderBatch> FetchAsync(CancellationToken cancellationToken)
    {
        var endpoint = BuildVendorOrderUri(_options);
        using var response = await httpClient.GetAsync(endpoint, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Shoprite VendorOrder returned HTTP {(int)response.StatusCode}."));
        }

        return ShopriteVendorOrderParser.Parse(body);
    }

    private static Uri BuildVendorOrderUri(ShopriteOptions options)
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Shoprite:BaseUrl must be a non-empty absolute HTTPS URI.");
        }

        if (string.IsNullOrWhiteSpace(options.Username))
        {
            throw new InvalidOperationException("Shoprite:Username is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Password))
        {
            throw new InvalidOperationException("Shoprite:Password is required.");
        }

        var baseWithTrailingSlash = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        var userName = Uri.EscapeDataString(options.Username);
        var password = Uri.EscapeDataString(options.Password);
        return new Uri(baseWithTrailingSlash, $"VendorOrder?userName={userName}&password={password}");
    }
}

