using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pvm.Application.Stock;
using Pvm.Infrastructure.Acumatica;

namespace Pvm.Infrastructure.Stock;

/// <summary>
/// Reads stock figures from Acumatica over the contract REST API, in one signed-in session.
/// Replaces the legacy script's direct SQL queries, which needed port 1433 and broke on upgrades.
/// </summary>
public sealed class AcumaticaStockReader(
    HttpClient httpClient,
    IOptions<AcumaticaOptions> acumaticaOptions,
    IOptions<StockSyncOptions> stockOptions,
    TimeProvider timeProvider) : IAcumaticaStockReader
{
    // Acumatica rejects a long OR-chain in one filter; this many order numbers per call is safe.
    private const int WebOrderBatchSize = 15;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly AcumaticaOptions _acumatica = acumaticaOptions.Value;
    private readonly StockSyncOptions _stock = stockOptions.Value;

    public async Task<AcumaticaStockSnapshot> ReadAsync(
        IReadOnlyCollection<string> inventoryCds,
        IReadOnlyCollection<string> webOrderIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inventoryCds);
        ArgumentNullException.ThrowIfNull(webOrderIds);
        if (string.IsNullOrWhiteSpace(_acumatica.BaseUrl)
            || string.IsNullOrWhiteSpace(_acumatica.Username)
            || string.IsNullOrWhiteSpace(_acumatica.Password))
        {
            throw new InvalidOperationException("Acumatica BaseUrl and credentials are required for the stock sync.");
        }

        var cookie = await SignInAsync(cancellationToken);
        try
        {
            var visible = await ReadVisibleItemsAsync(cookie, cancellationToken);
            var wanted = inventoryCds
                .Where(visible.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var (transfersIn, transfersOut) = await ReadUnreleasedTransfersAsync(cookie, cancellationToken);
            var openOrders = await ReadOpenOrderQuantitiesAsync(cookie, cancellationToken);

            var figures = new Dictionary<string, AcumaticaStockFigures>(StringComparer.OrdinalIgnoreCase);
            var failures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var inventoryCd in wanted)
            {
                var (allocation, error) = await ReadAllocationAsync(cookie, inventoryCd, cancellationToken);
                if (allocation is null)
                {
                    failures[inventoryCd] = error ?? "no response";
                    continue;
                }

                figures[inventoryCd] = new AcumaticaStockFigures(
                    inventoryCd,
                    allocation.Value.OnHand,
                    allocation.Value.Booked,
                    allocation.Value.Allocated,
                    openOrders.GetValueOrDefault(inventoryCd),
                    transfersIn.GetValueOrDefault(inventoryCd),
                    transfersOut.GetValueOrDefault(inventoryCd));
            }

            var loaded = await ReadLoadedWebOrdersAsync(cookie, webOrderIds, cancellationToken);
            return new AcumaticaStockSnapshot(visible, figures, failures, loaded);
        }
        finally
        {
            await SignOutAsync(cookie);
        }
    }

    private async Task<HashSet<string>> ReadVisibleItemsAsync(string? cookie, CancellationToken cancellationToken)
    {
        // An item hidden by a restriction group reads as zero in the inquiries. Listing what the
        // user can see is how a hidden item is told apart from one that is really out of stock.
        var rows = await GetArrayAsync(cookie, "StockItem?$select=InventoryID&$top=10000", "stock item list", cancellationToken);
        var visible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var id = Text(row, "InventoryID");
            if (!string.IsNullOrEmpty(id))
            {
                visible.Add(id);
            }
        }

        return visible;
    }

    private async Task<(Dictionary<string, decimal> In, Dictionary<string, decimal> Out)> ReadUnreleasedTransfersAsync(
        string? cookie,
        CancellationToken cancellationToken)
    {
        var rows = await GetArrayAsync(
            cookie,
            "TransferOrder?$filter=" + Uri.EscapeDataString("Status ne 'Released'") + "&$expand=Details&$top=10000",
            "unreleased transfer orders",
            cancellationToken);

        var incoming = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var outgoing = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in rows)
        {
            var from = Text(order, "FromWarehouseID");
            var to = Text(order, "ToWarehouseID");
            foreach (var line in Array(order, "Details"))
            {
                var inventoryCd = Text(line, "InventoryID");
                var quantity = Number(line, "Qty");
                if (string.IsNullOrEmpty(inventoryCd))
                {
                    continue;
                }

                if (Same(from, _stock.WarehouseId) && Same(Text(line, "FromLocationID"), _stock.LocationId))
                {
                    outgoing[inventoryCd] = outgoing.GetValueOrDefault(inventoryCd) + quantity;
                }

                if (Same(to, _stock.WarehouseId) && Same(Text(line, "ToLocationID"), _stock.LocationId))
                {
                    incoming[inventoryCd] = incoming.GetValueOrDefault(inventoryCd) + quantity;
                }
            }
        }

        return (incoming, outgoing);
    }

    private async Task<Dictionary<string, decimal>> ReadOpenOrderQuantitiesAsync(
        string? cookie,
        CancellationToken cancellationToken)
    {
        // Recent sales orders that have not shipped yet hold stock. An order with any shipment no
        // longer counts here: its quantity is in the allocation figures instead.
        var since = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime)
            .AddDays(-Math.Max(1, _stock.OpenOrderLookbackDays));
        var excluded = new HashSet<string>(_stock.ExcludedCustomers, StringComparer.OrdinalIgnoreCase);
        var totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var orderType in _stock.OpenOrderTypes)
        {
            var filter = $"OrderType eq '{orderType}' and Date ge datetimeoffset'"
                + since.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                + "T00:00:00" + _stock.AcumaticaUtcOffset + "'";
            var rows = await GetArrayAsync(
                cookie,
                "SalesOrder?$filter=" + Uri.EscapeDataString(filter) + "&$expand=Details,Shipments&$top=10000",
                $"{orderType} sales orders",
                cancellationToken);

            foreach (var order in rows)
            {
                if (excluded.Contains(Text(order, "CustomerID") ?? string.Empty) || Array(order, "Shipments").Any())
                {
                    continue;
                }

                foreach (var line in Array(order, "Details"))
                {
                    var inventoryCd = Text(line, "InventoryID");
                    if (!string.IsNullOrEmpty(inventoryCd))
                    {
                        totals[inventoryCd] = totals.GetValueOrDefault(inventoryCd) + Number(line, "OrderQty");
                    }
                }
            }
        }

        return totals;
    }

    private async Task<((decimal OnHand, decimal Booked, decimal Allocated)? Figures, string? Error)> ReadAllocationAsync(
        string? cookie,
        string inventoryCd,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, object>
        {
            ["InventoryID"] = new { value = inventoryCd },
            ["WarehouseID"] = new { value = _stock.WarehouseId },
            ["Location"] = new { value = _stock.LocationId },
        };
        using var request = Request(HttpMethod.Put, EntityUri("InventoryAllocationInquiry"), cookie);
        request.Content = new StringContent(JsonSerializer.Serialize(parameters, SerializerOptions), Encoding.UTF8, "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (null, $"HTTP {(int)response.StatusCode}");
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        return ((Number(root, "OnHand"), Number(root, "SOBooked"), Number(root, "SOAllocated")), null);
    }

    private async Task<List<LoadedWebOrderLine>> ReadLoadedWebOrdersAsync(
        string? cookie,
        IReadOnlyCollection<string> webOrderIds,
        CancellationToken cancellationToken)
    {
        var lines = new List<LoadedWebOrderLine>();
        foreach (var batch in webOrderIds.Distinct(StringComparer.Ordinal).Chunk(WebOrderBatchSize))
        {
            var filter = string.Join(" or ", batch.Select(id => $"CustomerOrder eq '{id.Replace("'", "''", StringComparison.Ordinal)}'"));
            var rows = await GetArrayAsync(
                cookie,
                "SalesOrder?$filter=" + Uri.EscapeDataString(filter) + "&$expand=Details&$top=1000",
                "sales orders for web orders",
                cancellationToken);
            foreach (var order in rows)
            {
                var webOrderId = Text(order, "CustomerOrder") ?? string.Empty;
                foreach (var line in Array(order, "Details"))
                {
                    var inventoryCd = Text(line, "InventoryID");
                    if (!string.IsNullOrEmpty(inventoryCd))
                    {
                        lines.Add(new LoadedWebOrderLine(webOrderId, inventoryCd, Number(line, "OrderQty")));
                    }
                }
            }
        }

        return lines;
    }

    private async Task<List<JsonElement>> GetArrayAsync(
        string? cookie,
        string relative,
        string what,
        CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, EntityUri(relative), cookie);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Acumatica read of {what} failed with status {(int)response.StatusCode} ({response.StatusCode}).");
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().Select(element => element.Clone()).ToList()
            : [];
    }

    private async Task<string?> SignInAsync(CancellationToken cancellationToken)
    {
        var login = new Dictionary<string, string>
        {
            ["name"] = _acumatica.Username!,
            ["password"] = _acumatica.Password!,
        };
        if (!string.IsNullOrWhiteSpace(_acumatica.Company))
        {
            login["company"] = _acumatica.Company;
        }

        using var response = await httpClient.PostAsJsonAsync(Root("entity/auth/login"), login, SerializerOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Acumatica sign-in failed with status {(int)response.StatusCode} ({response.StatusCode}).");
        }

        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            return null;
        }

        var baseUri = new Uri(_acumatica.BaseUrl!.TrimEnd('/') + "/");
        var jar = new CookieContainer();
        foreach (var setCookie in setCookies)
        {
            jar.SetCookies(baseUri, setCookie);
        }

        var header = jar.GetCookieHeader(baseUri);
        return string.IsNullOrWhiteSpace(header) ? null : header;
    }

    private async Task SignOutAsync(string? cookie)
    {
        // Licences cap concurrent API sessions, so the session is always released.
        using var request = Request(HttpMethod.Post, Root("entity/auth/logout"), cookie);
        request.Content = new StringContent(string.Empty);
        using var response = await httpClient.SendAsync(request, CancellationToken.None);
    }

    private Uri EntityUri(string relative)
        => Root($"entity/{_acumatica.EndpointName}/{_acumatica.EndpointVersion}/{relative}");

    private Uri Root(string relative) => new(new Uri(_acumatica.BaseUrl!.TrimEnd('/') + "/"), relative);

    private static HttpRequestMessage Request(HttpMethod method, Uri uri, string? cookie)
    {
        var request = new HttpRequestMessage(method, uri);
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        return request;
    }

    private static bool Same(string? left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<JsonElement> Array(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var field)
            && field.ValueKind == JsonValueKind.Object
            && field.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static decimal Number(JsonElement element, string name)
        => element.TryGetProperty(name, out var field)
            && field.ValueKind == JsonValueKind.Object
            && field.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.Number
            ? value.GetDecimal()
            : 0m;
}
