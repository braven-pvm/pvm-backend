using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pvm.Application.Reporting.SellOut;
using Pvm.Infrastructure.Acumatica;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Reporting.SellOut;

/// <summary>Reads one page of the contract API entity StockItem with its CrossReferences detail.</summary>
public static class ProductGtinParser
{
    public static IReadOnlyList<ProductGtin> ParsePage(string json)
    {
        using var document = Parse(json);
        var gtins = new List<ProductGtin>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var inventoryId = Value(item, "InventoryID");
            if (inventoryId is null
                || !item.TryGetProperty("CrossReferences", out var references)
                || references.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var reference in references.EnumerateArray())
            {
                var id = Value(reference, "AlternateID");
                if (IsGtin(id))
                {
                    gtins.Add(new ProductGtin(id!, inventoryId, Value(reference, "UOM")));
                }
            }
        }

        return gtins;
    }

    /// <summary>The stock items on the page, with or without a GTIN. A page with fewer items than asked is the last.</summary>
    public static int CountItems(string json)
    {
        using var document = Parse(json);
        return document.RootElement.GetArrayLength();
    }

    /// <summary>A GTIN has 8, 12, 13 or 14 digits.</summary>
    public static bool IsGtin(string? value) =>
        value is { Length: 8 or 12 or 13 or 14 } && value.All(char.IsAsciiDigit);

    private static JsonDocument Parse(string json)
    {
        var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            document.Dispose();
            throw new SellOutFormatException("The stock item list response is not a list.");
        }

        return document;
    }

    private static string? Value(JsonElement item, string name) =>
        item.TryGetProperty(name, out var field)
        && field.ValueKind == JsonValueKind.Object
        && field.TryGetProperty("value", out var value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() is { } text
        && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;
}

/// <summary>
/// Reads the GTINs of all stock items from the Acumatica contract API, a page at a time. The read
/// signs in, reads and signs out. It changes nothing in Acumatica.
/// </summary>
public sealed class AcumaticaProductGtinSource(HttpClient httpClient, IOptions<AcumaticaOptions> options) : IProductGtinSource
{
    private const int PageSize = 500;
    private readonly AcumaticaOptions _options = options.Value;

    public async Task<IReadOnlyList<ProductGtin>> ReadAllAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl) || string.IsNullOrWhiteSpace(_options.Username) || string.IsNullOrWhiteSpace(_options.Password))
        {
            throw new InvalidOperationException("Acumatica BaseUrl and credentials are required for the GTIN refresh.");
        }

        var cookie = await SignInAsync(cancellationToken);
        try
        {
            var gtins = new List<ProductGtin>();
            for (var skip = 0; ; skip += PageSize)
            {
                var select = Uri.EscapeDataString("InventoryID,CrossReferences/AlternateID,CrossReferences/AlternateType,CrossReferences/UOM");
                using var request = Request(HttpMethod.Get,
                    $"entity/{Uri.EscapeDataString(_options.EndpointName)}/{Uri.EscapeDataString(_options.EndpointVersion)}/StockItem"
                    + $"?$select={select}&$expand=CrossReferences&$top={PageSize}&$skip={skip}", cookie);
                using var response = await httpClient.SendAsync(request, cancellationToken);
                EnsureSuccess(response, "stock item read");
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                gtins.AddRange(ProductGtinParser.ParsePage(json));
                if (ProductGtinParser.CountItems(json) < PageSize)
                {
                    return gtins;
                }
            }
        }
        finally
        {
            using var logout = Request(HttpMethod.Post, "entity/auth/logout", cookie);
            using var _ = await httpClient.SendAsync(logout, CancellationToken.None);
        }
    }

    private async Task<string?> SignInAsync(CancellationToken cancellationToken)
    {
        var login = new Dictionary<string, string> { ["name"] = _options.Username!, ["password"] = _options.Password! };
        if (!string.IsNullOrWhiteSpace(_options.Company))
        {
            login["company"] = _options.Company;
        }

        if (!string.IsNullOrWhiteSpace(_options.Branch))
        {
            login["branch"] = _options.Branch;
        }

        using var response = await httpClient.PostAsJsonAsync(BuildUri("entity/auth/login"), login, cancellationToken);
        EnsureSuccess(response, "sign-in");
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            return null;
        }

        var baseUri = BuildUri(string.Empty);
        var cookies = new CookieContainer();
        foreach (var setCookie in setCookies)
        {
            cookies.SetCookies(baseUri, setCookie);
        }

        var header = cookies.GetCookieHeader(baseUri);
        return string.IsNullOrWhiteSpace(header) ? null : header;
    }

    private HttpRequestMessage Request(HttpMethod method, string relativePath, string? cookie)
    {
        var request = new HttpRequestMessage(method, BuildUri(relativePath));
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        return request;
    }

    private Uri BuildUri(string relativePath) => new(new Uri(_options.BaseUrl!.TrimEnd('/') + "/"), relativePath);

    private static void EnsureSuccess(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Acumatica answered {(int)response.StatusCode} to the {operation}.");
        }
    }
}

/// <summary>The outcome of one GTIN refresh.</summary>
/// <param name="Gtins">GTINs kept, each on one item.</param>
/// <param name="Ambiguous">GTINs on more than one item. The refresh keeps none of them.</param>
/// <param name="ProductsMapped">Sell-out product codes that the refresh mapped by GTIN.</param>
public sealed record ProductGtinRefreshResult(int Gtins, IReadOnlyList<string> Ambiguous, int ProductsMapped);

/// <summary>
/// Replaces the GTIN table with the GTINs of the Acumatica stock items, then maps the sell-out
/// rows that waited for their GTIN.
/// </summary>
public sealed class ProductGtinRefreshService(
    PvmDbContext db,
    IProductGtinSource source,
    TimeProvider timeProvider,
    ILogger<ProductGtinRefreshService> logger)
{
    public async Task<ProductGtinRefreshResult> RefreshAsync(CancellationToken cancellationToken)
    {
        var read = await source.ReadAllAsync(cancellationToken);
        if (read.Count == 0)
        {
            // An empty read is a defect in the read, not an empty item list. Keep the table as it is.
            throw new InvalidOperationException("The stock item read gave no GTINs. The GTIN table stays as it was.");
        }

        var byGtin = read.GroupBy(gtin => gtin.Gtin, StringComparer.Ordinal).ToList();
        var ambiguous = byGtin
            .Where(group => group.Select(gtin => gtin.InventoryId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(group => group.Key)
            .Order(StringComparer.Ordinal)
            .ToList();
        var current = byGtin
            .Where(group => !ambiguous.Contains(group.Key))
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var stored = await db.ProductGtins.ToDictionaryAsync(gtin => gtin.Gtin, StringComparer.Ordinal, cancellationToken);
        foreach (var (gtin, entity) in stored)
        {
            if (!current.ContainsKey(gtin))
            {
                db.ProductGtins.Remove(entity);
            }
        }

        foreach (var (gtin, item) in current)
        {
            if (!stored.TryGetValue(gtin, out var entity))
            {
                entity = new ProductGtinEntity { Gtin = gtin, InventoryId = item.InventoryId };
                db.ProductGtins.Add(entity);
            }

            entity.InventoryId = item.InventoryId;
            entity.Uom = item.Uom;
            entity.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        var mapped = await MapWaitingRowsAsync(now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        foreach (var gtin in ambiguous)
        {
            logger.LogWarning("product-gtin.ambiguous Gtin={Gtin}", gtin);
        }

        logger.LogInformation("product-gtin.refreshed Gtins={Gtins} Ambiguous={Ambiguous} ProductsMapped={Mapped}", current.Count, ambiguous.Count, mapped);
        return new ProductGtinRefreshResult(current.Count, ambiguous, mapped);
    }

    private async Task<int> MapWaitingRowsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var waiting = await db.SellOutMonths
            .Where(month => month.InventoryId == null && month.Gtin != null)
            .Select(month => new { month.Source, month.SourceProductCode, month.Gtin })
            .Distinct()
            .ToListAsync(cancellationToken);
        var gtins = waiting.Select(row => row.Gtin!).Distinct().ToList();
        var items = await db.ProductGtins.Where(gtin => gtins.Contains(gtin.Gtin))
            .ToDictionaryAsync(gtin => gtin.Gtin, gtin => gtin.InventoryId, StringComparer.Ordinal, cancellationToken);

        var mapped = 0;
        foreach (var product in waiting.GroupBy(row => (row.Source, row.SourceProductCode)))
        {
            var found = product.Select(row => items.GetValueOrDefault(row.Gtin!)).OfType<string>().Distinct().ToList();
            var hasMap = await db.SellOutProductMaps.AnyAsync(
                map => map.Source == product.Key.Source && map.SourceProductCode == product.Key.SourceProductCode, cancellationToken);
            if (found.Count != 1 || hasMap)
            {
                continue;
            }

            db.SellOutProductMaps.Add(new SellOutProductMapEntity
            {
                Source = product.Key.Source,
                SourceProductCode = product.Key.SourceProductCode,
                InventoryId = found[0],
                Method = SellOutMapMethods.Gtin,
                MappedBy = "system:gtin-refresh",
                MappedAt = now,
            });
            await db.SaveChangesAsync(cancellationToken);
            await db.SellOutMonths
                .Where(month => month.Source == product.Key.Source && month.SourceProductCode == product.Key.SourceProductCode)
                .ExecuteUpdateAsync(set => set.SetProperty(month => month.InventoryId, found[0]), cancellationToken);
            await db.StoreStockSnapshots
                .Where(stock => stock.Source == product.Key.Source && stock.SourceProductCode == product.Key.SourceProductCode)
                .ExecuteUpdateAsync(set => set.SetProperty(stock => stock.InventoryId, found[0]), cancellationToken);
            mapped++;
        }

        return mapped;
    }
}
