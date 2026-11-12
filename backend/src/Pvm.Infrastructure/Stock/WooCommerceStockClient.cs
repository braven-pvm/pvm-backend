using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pvm.Application.Stock;

namespace Pvm.Infrastructure.Stock;

/// <summary>Connection to the WooCommerce REST API of the web store.</summary>
public sealed record WooCommerceOptions
{
    public const string SectionName = "WooCommerce";

    /// <summary>The store root, for example <c>https://www.pvm.co.za</c>.</summary>
    public string? BaseUrl { get; init; }

    public string? ConsumerKey { get; init; }

    public string? ConsumerSecret { get; init; }

    /// <summary>Order statuses whose lines still hold stock in the store.</summary>
    public string PendingOrderStatuses { get; init; } = "on-hold,processing";

    public int PageSize { get; init; } = 100;

    /// <summary>Safety cap on pages walked per listing.</summary>
    public int MaxPages { get; init; } = 50;
}

/// <summary>
/// Reads and sets stock through the WooCommerce REST API. Products are found by SKU, so the store
/// and Acumatica are matched on the code they share, with no mapping table to fall out of date.
/// </summary>
public sealed class WooCommerceStockClient(HttpClient httpClient, IOptions<WooCommerceOptions> options)
    : IWooCommerceStockClient
{
    private readonly WooCommerceOptions _options = options.Value;

    public async Task<IReadOnlyList<WebStockItem>> ListStockItemsAsync(CancellationToken cancellationToken)
    {
        var items = new List<WebStockItem>();
        await foreach (var product in PagesAsync("products", "status=any", cancellationToken))
        {
            var productId = product.GetProperty("id").GetInt64();
            var sku = Sku(product);
            if (sku is not null)
            {
                items.Add(new WebStockItem(sku, productId, null, Management(product), Quantity(product)));
            }

            if (string.Equals(Str(product, "type"), "variable", StringComparison.Ordinal))
            {
                await foreach (var variation in PagesAsync($"products/{productId}/variations", null, cancellationToken))
                {
                    var variationSku = Sku(variation);
                    if (variationSku is not null)
                    {
                        items.Add(new WebStockItem(
                            variationSku,
                            productId,
                            variation.GetProperty("id").GetInt64(),
                            Management(variation),
                            Quantity(variation)));
                    }
                }
            }
        }

        return items;
    }

    public async Task<IReadOnlyList<WebOrderLine>> ListPendingOrderLinesAsync(CancellationToken cancellationToken)
    {
        var lines = new List<WebOrderLine>();
        await foreach (var order in PagesAsync("orders", "status=" + _options.PendingOrderStatuses, cancellationToken))
        {
            var orderId = order.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture);
            if (!order.TryGetProperty("line_items", out var lineItems) || lineItems.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var line in lineItems.EnumerateArray())
            {
                var sku = Sku(line);
                if (sku is not null && line.TryGetProperty("quantity", out var quantity) && quantity.ValueKind == JsonValueKind.Number)
                {
                    lines.Add(new WebOrderLine(orderId, sku, quantity.GetDecimal()));
                }
            }
        }

        return lines;
    }

    public async Task SetStockQuantityAsync(WebStockItem item, int quantity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Management != WebStockManagement.Own)
        {
            throw new InvalidOperationException($"{item.Sku} does not hold its own stock in the web store.");
        }

        var path = item.VariationId is { } variationId
            ? $"products/{item.ProductId}/variations/{variationId}"
            : $"products/{item.ProductId}";
        using var request = Request(HttpMethod.Put, path, null);
        request.Content = new StringContent(
            "{\"stock_quantity\":" + quantity.ToString(CultureInfo.InvariantCulture) + "}",
            Encoding.UTF8,
            "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureJsonAsync(response, $"stock update for {item.Sku}", cancellationToken);
    }

    private async IAsyncEnumerable<JsonElement> PagesAsync(
        string path,
        string? query,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var page = 1; page <= _options.MaxPages; page++)
        {
            var pageQuery = $"per_page={_options.PageSize}&page={page}" + (query is null ? string.Empty : "&" + query);
            using var request = Request(HttpMethod.Get, path, pageQuery);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            using var document = JsonDocument.Parse(await EnsureJsonAsync(response, path, cancellationToken));
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
            {
                yield break;
            }

            foreach (var element in document.RootElement.EnumerateArray())
            {
                yield return element.Clone();
            }

            if (document.RootElement.GetArrayLength() < _options.PageSize)
            {
                yield break;
            }
        }

        // Every page came back full: there may be more, and a partial list must not be used.
        throw new InvalidOperationException(
            $"WooCommerce {path} filled all {_options.MaxPages} pages of {_options.PageSize}. The list may be incomplete, so the run stops.");
    }

    private HttpRequestMessage Request(HttpMethod method, string path, string? query)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl)
            || string.IsNullOrWhiteSpace(_options.ConsumerKey)
            || string.IsNullOrWhiteSpace(_options.ConsumerSecret))
        {
            throw new InvalidOperationException("WooCommerce BaseUrl, ConsumerKey and ConsumerSecret are required.");
        }

        var uri = new Uri(_options.BaseUrl.TrimEnd('/') + "/wp-json/wc/v3/" + path + (query is null ? string.Empty : "?" + query));
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ConsumerKey}:{_options.ConsumerSecret}")));
        return request;
    }

    private static async Task<string> EnsureJsonAsync(HttpResponseMessage response, string what, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"WooCommerce {what} failed with status {(int)response.StatusCode} ({response.StatusCode}).");
        }

        // A bot-protection page answers 200 with HTML. Treat anything that is not JSON as a failure,
        // so that a blocked read can never look like an empty store.
        if (response.Content.Headers.ContentType?.MediaType is not { } media
            || !media.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"WooCommerce {what} returned {response.Content.Headers.ContentType?.MediaType ?? "no content type"}, not JSON. "
                + "The site's bot protection may be blocking the API.");
        }

        return body;
    }

    private static string? Sku(JsonElement element)
    {
        var sku = Str(element, "sku");
        return string.IsNullOrWhiteSpace(sku) ? null : sku.Trim();
    }

    private static string? Str(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Quantity(JsonElement element)
        => element.TryGetProperty("stock_quantity", out var value) && value.ValueKind == JsonValueKind.Number
            ? (int)Math.Floor(value.GetDecimal())
            : null;

    // manage_stock is true, false, or the string "parent" on a variation that uses the parent's stock.
    private static WebStockManagement Management(JsonElement element)
    {
        if (!element.TryGetProperty("manage_stock", out var value))
        {
            return WebStockManagement.None;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => WebStockManagement.Own,
            JsonValueKind.String when string.Equals(value.GetString(), "parent", StringComparison.Ordinal) => WebStockManagement.Parent,
            _ => WebStockManagement.None,
        };
    }
}
