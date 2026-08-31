using System.Globalization;
using System.Net.Http.Json;
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
        var endpoint = BuildVendorOrderUri(_options, action: null);
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

    public Task AcknowledgeAsync(
        IReadOnlyCollection<string> purchaseOrderNumbers,
        CancellationToken cancellationToken)
        => SendOrderActionAsync("A", purchaseOrderNumbers, cancellationToken);

    public Task ResetAsync(
        IReadOnlyCollection<string> purchaseOrderNumbers,
        CancellationToken cancellationToken)
        => SendOrderActionAsync("Reset", purchaseOrderNumbers, cancellationToken);

    private async Task SendOrderActionAsync(
        string action,
        IReadOnlyCollection<string> purchaseOrderNumbers,
        CancellationToken cancellationToken)
    {
        if (purchaseOrderNumbers.Count == 0)
        {
            return;
        }

        var orderNumbers = new List<long>(purchaseOrderNumbers.Count);
        foreach (var purchaseOrderNumber in purchaseOrderNumbers)
        {
            if (!long.TryParse(
                    purchaseOrderNumber?.Trim(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var orderNumber))
            {
                throw new InvalidOperationException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Shoprite order number '{purchaseOrderNumber}' is not numeric and cannot be acknowledged."));
            }

            orderNumbers.Add(orderNumber);
        }

        var endpoint = BuildVendorOrderUri(_options, action);
        using var response = await httpClient.PutAsJsonAsync(endpoint, orderNumbers, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Shoprite VendorOrder?action={action} returned HTTP {(int)response.StatusCode}. {Trim(body)}"));
        }
    }

    private static string Trim(string? body)
        => string.IsNullOrWhiteSpace(body)
            ? string.Empty
            : body.Length <= 500 ? body : body[..500];

    private static Uri BuildVendorOrderUri(ShopriteOptions options, string? action)
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
        var query = $"VendorOrder?userName={userName}&password={password}";
        if (!string.IsNullOrWhiteSpace(action))
        {
            query += $"&action={Uri.EscapeDataString(action)}";
        }

        return new Uri(baseWithTrailingSlash, query);
    }
}
