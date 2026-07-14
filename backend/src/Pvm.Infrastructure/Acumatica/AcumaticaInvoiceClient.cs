using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pvm.Application.Acumatica;

namespace Pvm.Infrastructure.Acumatica;

public sealed class AcumaticaInvoiceClient(
    HttpClient httpClient,
    IOptions<AcumaticaOptions> options) : IAcumaticaInvoiceClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly AcumaticaOptions _options = options.Value;

    public async Task<IReadOnlyList<AcumaticaInvoiceDto>> FetchFinalizedInvoicesAsync(
        CancellationToken cancellationToken)
    {
        ValidateOptions(_options);
        var loginSucceeded = false;
        string? sessionCookie = null;

        try
        {
            var login = new Dictionary<string, string>
            {
                ["name"] = _options.Username!,
                ["password"] = _options.Password!
            };
            if (!string.IsNullOrWhiteSpace(_options.Company))
            {
                login["company"] = _options.Company;
            }

            if (!string.IsNullOrWhiteSpace(_options.Branch))
            {
                login["branch"] = _options.Branch;
            }

            using var loginResponse = await httpClient.PostAsJsonAsync(
                BuildUri("entity/auth/login"),
                login,
                SerializerOptions,
                cancellationToken);
            EnsureSuccess(loginResponse, "sign-in");
            loginSucceeded = true;
            sessionCookie = ReadSessionCookie(loginResponse);

            var invoices = new List<AcumaticaInvoiceDto>();
            var skip = 0;

            while (true)
            {
                using var invoiceRequest = CreateSessionRequest(
                    HttpMethod.Get,
                    BuildInvoicePageUri(skip),
                    sessionCookie);
                using var invoiceResponse = await httpClient.SendAsync(
                    invoiceRequest,
                    cancellationToken);
                EnsureSuccess(invoiceResponse, "sales invoice retrieval");

                await using var content = await invoiceResponse.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidOperationException("Acumatica sales invoice retrieval returned an unexpected payload shape.");
                }

                var page = document.RootElement.EnumerateArray().ToArray();
                invoices.AddRange(page
                    .Where(IsFinalized)
                    .Select(MapInvoice));

                if (page.Length < _options.PageSize)
                {
                    return invoices;
                }

                skip += _options.PageSize;
            }
        }
        finally
        {
            if (loginSucceeded)
            {
                using var logoutRequest = CreateSessionRequest(
                    HttpMethod.Post,
                    BuildUri("entity/auth/logout"),
                    sessionCookie);
                using var logoutResponse = await httpClient.SendAsync(
                    logoutRequest,
                    cancellationToken);
                EnsureSuccess(logoutResponse, "sign-out");
            }
        }
    }

    private string? ReadSessionCookie(HttpResponseMessage loginResponse)
    {
        if (!loginResponse.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders))
        {
            return null;
        }

        var baseUri = new Uri(_options.BaseUrl!.TrimEnd('/') + "/");
        var cookies = new CookieContainer();
        foreach (var setCookie in setCookieHeaders)
        {
            cookies.SetCookies(baseUri, setCookie);
        }

        var value = cookies.GetCookieHeader(baseUri);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static HttpRequestMessage CreateSessionRequest(
        HttpMethod method,
        Uri uri,
        string? sessionCookie)
    {
        var request = new HttpRequestMessage(method, uri);
        if (!string.IsNullOrWhiteSpace(sessionCookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", sessionCookie);
        }

        return request;
    }

    private Uri BuildUri(string relativePath)
        => new(new Uri(_options.BaseUrl!.TrimEnd('/') + "/"), relativePath);

    private Uri BuildInvoicePageUri(int skip)
    {
        var statusFilter = "(Status eq 'Open' or Status eq 'Closed')";
        var customerFilter = string.Join(
            " or ",
            _options.CustomerAccounts.Select(account =>
                $"CustomerID eq '{account.Replace("'", "''", StringComparison.Ordinal)}'"));
        var filter = $"{statusFilter} and ({customerFilter})";
        var endpoint = $"entity/{Uri.EscapeDataString(_options.EndpointName)}/{Uri.EscapeDataString(_options.EndpointVersion)}/SalesInvoice";
        var query = $"?$expand=Details&$filter={Uri.EscapeDataString(filter)}&$top={_options.PageSize}&$skip={skip}";
        return BuildUri(endpoint + query);
    }

    private static bool IsFinalized(JsonElement source)
    {
        var status = String(source, "Status");
        return string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Closed", StringComparison.OrdinalIgnoreCase);
    }

    private AcumaticaInvoiceDto MapInvoice(JsonElement source)
    {
        var invoiceNumber = RequiredString(source, "ReferenceNbr");
        var amount = RequiredDecimal(source, "Amount");
        var taxTotal = Decimal(source, "TaxTotal") ?? 0m;
        var details = source.TryGetProperty("Details", out var detailArray) &&
            detailArray.ValueKind == JsonValueKind.Array
                ? detailArray.EnumerateArray().Select(MapLine).ToArray()
                : [];

        return new AcumaticaInvoiceDto(
            Id: DirectString(source, "id") ?? $"SalesInvoice:{RequiredString(source, "Type")}:{invoiceNumber}",
            InvoiceNumber: invoiceNumber,
            Status: RequiredString(source, "Status"),
            CustomerAccount: RequiredString(source, "CustomerID"),
            CustomerLocation: String(source, "LocationID") ?? String(source, "CustomerLocationID"),
            CustomerOrder: String(source, "CustomerOrder"),
            CurrencyCode: RequiredString(source, "Currency"),
            CountryCode: _options.CountryCode,
            InvoiceDate: RequiredDateTimeOffset(source, "Date"),
            TotalExcludingTax: Decimal(source, "DetailTotal") ?? amount - taxTotal,
            TotalIncludingTax: amount,
            TotalTax: taxTotal,
            Lines: details);
    }

    private static AcumaticaInvoiceLineDto MapLine(JsonElement source)
    {
        var quantity = RequiredDecimal(source, "Qty", "Quantity");
        var extendedPrice = Decimal(source, "ExtendedPrice", "Amount") ?? 0m;
        var unitExcludingTax = Decimal(source, "UnitPrice") ??
            (quantity == 0m ? 0m : extendedPrice / quantity);
        var taxAmount = Decimal(source, "TaxAmount") ?? 0m;
        var unitTaxAmount = quantity == 0m ? 0m : taxAmount / quantity;
        var taxPercentage = Decimal(source, "TaxRate") ??
            (extendedPrice == 0m ? 0m : decimal.Round(taxAmount / extendedPrice * 100m, 4));
        var unitIncludingTax = quantity == 0m
            ? unitExcludingTax
            : (extendedPrice + taxAmount) / quantity;

        return new AcumaticaInvoiceLineDto(
            LineNumber: RequiredInt32(source, "LineNbr", "LineNumber"),
            InventoryId: RequiredString(source, "InventoryID"),
            Gtin: String(source, "GTIN", "Gtin", "Barcode"),
            Description: String(source, "Description", "TransactionDescription") ?? string.Empty,
            Quantity: quantity,
            Uom: RequiredString(source, "UOM", "Uom"),
            PackSize: Decimal(source, "PackSize"),
            UnitAmountExcludingTax: unitExcludingTax,
            UnitAmountIncludingTax: unitIncludingTax,
            TaxAmount: unitTaxAmount,
            TaxCategoryCode: String(source, "TaxCategory", "TaxCategoryID"),
            TaxPercentage: taxPercentage,
            IsCatchWeight: false);
    }

    private static string RequiredString(JsonElement source, params string[] names)
        => String(source, names) ?? throw MissingField(names);

    private static string? String(JsonElement source, params string[] names)
    {
        var value = Value(source, names);
        return value is { ValueKind: JsonValueKind.String }
            ? value.Value.GetString()
            : null;
    }

    private static string? DirectString(JsonElement source, string name)
        => source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal RequiredDecimal(JsonElement source, params string[] names)
        => Decimal(source, names) ?? throw MissingField(names);

    private static decimal? Decimal(JsonElement source, params string[] names)
    {
        var value = Value(source, names);
        return value is { ValueKind: JsonValueKind.Number } && value.Value.TryGetDecimal(out var result)
            ? result
            : null;
    }

    private static int RequiredInt32(JsonElement source, params string[] names)
    {
        var value = Value(source, names);
        return value is { ValueKind: JsonValueKind.Number } && value.Value.TryGetInt32(out var result)
            ? result
            : throw MissingField(names);
    }

    private static DateTimeOffset RequiredDateTimeOffset(JsonElement source, params string[] names)
    {
        var value = String(source, names);
        return DateTimeOffset.TryParse(value, out var result)
            ? result
            : throw MissingField(names);
    }

    private static JsonElement? Value(JsonElement source, params string[] names)
    {
        foreach (var name in names)
        {
            if (!source.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Object &&
                property.TryGetProperty("value", out var wrapped))
            {
                return wrapped;
            }

            return property;
        }

        return null;
    }

    private static InvalidOperationException MissingField(IEnumerable<string> names)
        => new($"Acumatica sales invoice payload is missing required field {string.Join(" or ", names)}.");

    private static void EnsureSuccess(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Acumatica {operation} returned HTTP {(int)response.StatusCode}.");
        }
    }

    private static void ValidateOptions(AcumaticaOptions options)
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Acumatica:BaseUrl must be a non-empty absolute HTTPS URI.");
        }

        Require(options.Username, "Username");
        Require(options.Password, "Password");
        Require(options.EndpointName, "EndpointName");
        Require(options.EndpointVersion, "EndpointVersion");

        if (options.CustomerAccounts.Length == 0 ||
            options.CustomerAccounts.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("Acumatica:CustomerAccounts must contain at least one customer account.");
        }

        if (options.PageSize is < 1 or > 1000)
        {
            throw new InvalidOperationException("Acumatica:PageSize must be between 1 and 1000.");
        }
    }

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Acumatica:{name} is required.");
        }
    }
}
