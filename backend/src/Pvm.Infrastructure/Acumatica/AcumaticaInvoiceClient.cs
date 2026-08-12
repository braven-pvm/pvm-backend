using System.Globalization;
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
    private const int CustomerAccountChunkSize = 20;
    private const int InventoryItemChunkSize = 20;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly AcumaticaOptions _options = options.Value;

    public Task<IReadOnlyList<AcumaticaInvoiceDto>> FetchFinalizedInvoicesAsync(
        CancellationToken cancellationToken)
        => FetchFinalizedInvoicesAsync(query: null, cancellationToken);

    public async Task<IReadOnlyList<AcumaticaInvoiceDto>> FetchFinalizedInvoicesAsync(
        AcumaticaInvoiceQuery? query,
        CancellationToken cancellationToken)
    {
        ValidateOptions(_options);
        if (query is not null && query.ModifiedFromInclusive >= query.ModifiedToExclusive)
        {
            throw new ArgumentException("The Acumatica modification window must have a positive duration.", nameof(query));
        }

        return await WithSessionAsync(async sessionCookie =>
        {
            var customerAccounts = await ResolveCustomerAccountsAsync(
                sessionCookie,
                cancellationToken);
            var invoices = new List<AcumaticaInvoiceDto>();

            foreach (var accountChunk in customerAccounts.Chunk(CustomerAccountChunkSize))
            {
                var skip = 0;
                while (true)
                {
                    using var invoiceRequest = CreateSessionRequest(
                        HttpMethod.Get,
                        BuildInvoicePageUri(accountChunk, skip, query),
                        sessionCookie);
                    using var invoiceResponse = await httpClient.SendAsync(
                        invoiceRequest,
                        cancellationToken);
                    EnsureSuccess(invoiceResponse, "sales invoice retrieval");

                    var page = await ReadArrayAsync(invoiceResponse, "sales invoice retrieval", cancellationToken);
                    foreach (var summary in page.Where(IsFinalizedInvoice))
                    {
                        var id = DirectString(summary, "id") ?? throw MissingField(["id"]);
                        using var detailRequest = CreateSessionRequest(
                            HttpMethod.Get,
                            BuildInvoiceDetailUri(id),
                            sessionCookie);
                        using var detailResponse = await httpClient.SendAsync(detailRequest, cancellationToken);
                        EnsureSuccess(detailResponse, "sales invoice detail retrieval");
                        var detail = await ReadObjectAsync(
                            detailResponse,
                            "sales invoice detail retrieval",
                            cancellationToken);
                        invoices.Add(MapInvoice(detail));
                    }

                    if (page.Length < _options.PageSize)
                    {
                        break;
                    }

                    skip += _options.PageSize;
                }
            }

            return await EnrichMissingGtinsAsync(invoices, sessionCookie, cancellationToken);
        }, cancellationToken);
    }

    public async Task<AcumaticaInvoiceDto?> FetchFinalizedInvoiceAsync(
        string invoiceId,
        CancellationToken cancellationToken)
    {
        ValidateOptions(_options);
        if (string.IsNullOrWhiteSpace(invoiceId))
        {
            throw new ArgumentException("Acumatica invoice ID is required.", nameof(invoiceId));
        }

        return await WithSessionAsync(async sessionCookie =>
        {
            var customerAccounts = await ResolveCustomerAccountsAsync(
                sessionCookie,
                cancellationToken);
            using var request = CreateSessionRequest(
                HttpMethod.Get,
                BuildInvoiceDetailUri(invoiceId),
                sessionCookie);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            EnsureSuccess(response, "sales invoice detail retrieval");
            var detail = await ReadObjectAsync(response, "sales invoice detail retrieval", cancellationToken);
            if (!IsFinalizedInvoice(detail))
            {
                return null;
            }

            var invoice = AssertSingle(await EnrichMissingGtinsAsync(
                [MapInvoice(detail)],
                sessionCookie,
                cancellationToken));
            return customerAccounts.Contains(invoice.CustomerAccount, StringComparer.OrdinalIgnoreCase)
                ? invoice
                : null;
        }, cancellationToken);
    }

    public async Task<AcumaticaInventoryItemDto?> FetchInventoryItemAsync(
        string inventoryId,
        CancellationToken cancellationToken)
    {
        ValidateOptions(_options);
        if (string.IsNullOrWhiteSpace(inventoryId))
        {
            throw new ArgumentException("Acumatica inventory ID is required.", nameof(inventoryId));
        }

        var normalizedInventoryId = inventoryId.Trim();
        return await WithSessionAsync(async sessionCookie =>
        {
            using var request = CreateSessionRequest(
                HttpMethod.Get,
                BuildStockItemLookupUri(normalizedInventoryId),
                sessionCookie);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            EnsureSuccess(response, "stock item lookup");
            var items = await ReadArrayAsync(response, "stock item lookup", cancellationToken);
            var item = items.SingleOrDefault(source => string.Equals(
                String(source, "InventoryID"),
                normalizedInventoryId,
                StringComparison.OrdinalIgnoreCase));
            return item.ValueKind == JsonValueKind.Undefined ? null : MapInventoryItem(item);
        }, cancellationToken);
    }

    private async Task<IReadOnlyList<AcumaticaInvoiceDto>> EnrichMissingGtinsAsync(
        IReadOnlyList<AcumaticaInvoiceDto> invoices,
        string? sessionCookie,
        CancellationToken cancellationToken)
    {
        var inventoryIds = invoices
            .SelectMany(invoice => invoice.Lines)
            .Where(line => string.IsNullOrWhiteSpace(line.Gtin))
            .Select(line => line.InventoryId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (inventoryIds.Length == 0)
        {
            return invoices;
        }

        var crossReferences = new Dictionary<string, List<InventoryCrossReference>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in inventoryIds.Chunk(InventoryItemChunkSize))
        {
            using var request = CreateSessionRequest(
                HttpMethod.Get,
                BuildStockItemPageUri(chunk),
                sessionCookie);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            EnsureSuccess(response, "stock item retrieval");
            var stockItems = await ReadArrayAsync(response, "stock item retrieval", cancellationToken);

            foreach (var stockItem in stockItems)
            {
                var inventoryId = RequiredString(stockItem, "InventoryID");
                var references = Array(stockItem, "CrossReferences")
                    .Select(MapInventoryCrossReference)
                    .Where(reference => reference is not null)
                    .Cast<InventoryCrossReference>()
                    .ToList();
                crossReferences[inventoryId] = references;
            }
        }

        return invoices.Select(invoice => invoice with
        {
            Lines = invoice.Lines.Select(line =>
            {
                if (!string.IsNullOrWhiteSpace(line.Gtin)
                    || !crossReferences.TryGetValue(line.InventoryId, out var references))
                {
                    return line;
                }

                return line with { Gtin = SelectGtin(references, line.Uom) };
            }).ToArray()
        }).ToArray();
    }

    private async Task<T> WithSessionAsync<T>(
        Func<string?, Task<T>> operation,
        CancellationToken cancellationToken)
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
        var sessionCookie = ReadSessionCookie(loginResponse);

        try
        {
            return await operation(sessionCookie);
        }
        finally
        {
            using var logoutRequest = CreateSessionRequest(
                HttpMethod.Post,
                BuildUri("entity/auth/logout"),
                sessionCookie);
            using var logoutResponse = await httpClient.SendAsync(logoutRequest, CancellationToken.None);
            EnsureSuccess(logoutResponse, "sign-out");
        }
    }

    private async Task<string[]> ResolveCustomerAccountsAsync(
        string? sessionCookie,
        CancellationToken cancellationToken)
    {
        var accounts = new HashSet<string>(
            _options.CustomerAccounts.Where(account => !string.IsNullOrWhiteSpace(account)),
            StringComparer.OrdinalIgnoreCase);
        foreach (var parent in _options.ParentCustomerAccounts.Where(parent => !string.IsNullOrWhiteSpace(parent)))
        {
            accounts.Add(parent);
        }

        if (_options.ParentCustomerAccounts.Length == 0)
        {
            return accounts.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        var skip = 0;
        while (true)
        {
            using var customerRequest = CreateSessionRequest(
                HttpMethod.Get,
                BuildCustomerPageUri(skip),
                sessionCookie);
            using var customerResponse = await httpClient.SendAsync(customerRequest, cancellationToken);
            EnsureSuccess(customerResponse, "customer account retrieval");
            var page = await ReadArrayAsync(customerResponse, "customer account retrieval", cancellationToken);

            foreach (var customer in page)
            {
                accounts.Add(RequiredString(customer, "CustomerID"));
            }

            if (page.Length < _options.PageSize)
            {
                break;
            }

            skip += _options.PageSize;
        }

        return accounts.Order(StringComparer.OrdinalIgnoreCase).ToArray();
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

    private Uri BuildCustomerPageUri(int skip)
    {
        var parentFilter = string.Join(
            " or ",
            _options.ParentCustomerAccounts.Select(parent =>
                $"ParentRecord eq '{EscapeFilterValue(parent)}'"));
        var endpoint = BuildEntityEndpoint("Customer");
        var query = $"?$select=CustomerID&$filter={Uri.EscapeDataString($"({parentFilter})")}&$top={_options.PageSize}&$skip={skip}";
        return BuildUri(endpoint + query);
    }

    private Uri BuildInvoicePageUri(
        IReadOnlyCollection<string> customerAccounts,
        int skip,
        AcumaticaInvoiceQuery? queryWindow)
    {
        var statusFilter = "(Status eq 'Open' or Status eq 'Closed')";
        var customerFilter = string.Join(
            " or ",
            customerAccounts.Select(account =>
                $"CustomerID eq '{EscapeFilterValue(account)}'"));
        var dateFrom = _options.InvoiceDateFrom!.Value.ToString(
            "yyyy-MM-dd'T'HH:mm:sszzz",
            CultureInfo.InvariantCulture);
        var modifiedFilter = queryWindow is null
            ? string.Empty
            : $" and LastModifiedDateTime ge datetimeoffset'{FormatDateTime(queryWindow.ModifiedFromInclusive)}'" +
              $" and LastModifiedDateTime lt datetimeoffset'{FormatDateTime(queryWindow.ModifiedToExclusive)}'";
        var filter = $"Type eq 'Invoice' and {statusFilter} and Date ge datetimeoffset'{dateFrom}'{modifiedFilter} and ({customerFilter})";
        var orderBy = Uri.EscapeDataString("LastModifiedDateTime asc,ReferenceNbr asc");
        var query = $"?$filter={Uri.EscapeDataString(filter)}&$orderby={orderBy}&$top={_options.PageSize}&$skip={skip}";
        return BuildUri(BuildEntityEndpoint("SalesInvoice") + query);
    }

    private static string FormatDateTime(DateTimeOffset value)
        => value.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);

    private Uri BuildInvoiceDetailUri(string id)
    {
        var endpoint = $"{BuildEntityEndpoint("SalesInvoice")}/{Uri.EscapeDataString(id)}";
        return BuildUri(endpoint + "?$expand=Details,TaxDetails");
    }

    private Uri BuildStockItemPageUri(IReadOnlyCollection<string> inventoryIds)
    {
        var filter = string.Join(
            " or ",
            inventoryIds.Select(inventoryId =>
                $"InventoryID eq '{EscapeFilterValue(inventoryId)}'"));
        var query = $"?$filter={Uri.EscapeDataString($"({filter})")}&$expand=CrossReferences&$top={inventoryIds.Count}";
        return BuildUri(BuildEntityEndpoint("StockItem") + query);
    }

    private Uri BuildStockItemLookupUri(string inventoryId)
    {
        var filter = $"InventoryID eq '{EscapeFilterValue(inventoryId)}'";
        var fields = Uri.EscapeDataString("InventoryID,Description,ItemStatus,BaseUOM,SalesUOM,PurchaseUOM");
        var query = $"?$select={fields}&$filter={Uri.EscapeDataString(filter)}&$top=2";
        return BuildUri(BuildEntityEndpoint("StockItem") + query);
    }

    private string BuildEntityEndpoint(string entity)
        => $"entity/{Uri.EscapeDataString(_options.EndpointName)}/{Uri.EscapeDataString(_options.EndpointVersion)}/{entity}";

    private static string EscapeFilterValue(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private static bool IsFinalizedInvoice(JsonElement source)
    {
        var type = String(source, "Type");
        var status = String(source, "Status");
        return string.Equals(type, "Invoice", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(status, "Closed", StringComparison.OrdinalIgnoreCase));
    }

    private static AcumaticaInventoryItemDto MapInventoryItem(JsonElement source)
    {
        var units = new[]
            {
                String(source, "SalesUOM"),
                String(source, "BaseUOM"),
                String(source, "PurchaseUOM")
            }
            .Where(unit => !string.IsNullOrWhiteSpace(unit))
            .Select(unit => unit!.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new AcumaticaInventoryItemDto(
            RequiredString(source, "InventoryID"),
            String(source, "Description") ?? string.Empty,
            String(source, "ItemStatus", "Status"),
            units);
    }

    private AcumaticaInvoiceDto MapInvoice(JsonElement source)
    {
        var invoiceNumber = RequiredString(source, "ReferenceNbr");
        var totalIncludingTax = RequiredDecimal(source, "Amount");
        var totalTax = Decimal(source, "TaxTotal") ?? 0m;
        var totalExcludingTax = totalIncludingTax - totalTax;
        var details = Array(source, "Details");
        var taxDetails = Array(source, "TaxDetails").Select(MapTaxDetail).ToArray();
        var lineWeights = details.Select(LineAmount).ToArray();
        var lineExcludingTax = AllocateCurrency(totalExcludingTax, lineWeights);
        var taxRates = details.Select(detail => TaxRate(detail, taxDetails, totalExcludingTax, totalTax)).ToArray();
        var taxWeights = lineExcludingTax
            .Select((amount, index) => amount * taxRates[index])
            .ToArray();
        var lineTax = AllocateCurrency(totalTax, taxWeights);

        var lines = details
            .Select((detail, index) => MapLine(
                detail,
                lineExcludingTax[index],
                lineTax[index],
                taxRates[index]))
            .ToArray();

        return new AcumaticaInvoiceDto(
            Id: DirectString(source, "id") ?? $"SalesInvoice:{RequiredString(source, "Type")}:{invoiceNumber}",
            InvoiceNumber: invoiceNumber,
            Status: RequiredString(source, "Status"),
            CustomerAccount: RequiredString(source, "CustomerID"),
            CustomerLocation: String(source, "LocationID", "CustomerLocationID"),
            CustomerOrder: String(source, "CustomerOrder"),
            CurrencyCode: RequiredString(source, "Currency"),
            CountryCode: _options.CountryCode,
            InvoiceDate: RequiredDateTimeOffset(source, "Date"),
            TotalExcludingTax: totalExcludingTax,
            TotalIncludingTax: totalIncludingTax,
            TotalTax: totalTax,
            Lines: lines,
            LastModifiedAt: DateTimeOffsetValue(source, "LastModifiedDateTime", "LastModifiedDate"));
    }

    private static AcumaticaInvoiceLineDto MapLine(
        JsonElement source,
        decimal lineExcludingTax,
        decimal lineTax,
        decimal taxPercentage)
    {
        var quantity = RequiredDecimal(source, "Qty", "Quantity");
        var unitExcludingTax = quantity == 0m ? 0m : lineExcludingTax / quantity;
        var unitTaxAmount = quantity == 0m ? 0m : lineTax / quantity;
        var unitIncludingTax = quantity == 0m ? 0m : (lineExcludingTax + lineTax) / quantity;

        return new AcumaticaInvoiceLineDto(
            LineNumber: RequiredInt32(source, "LineNbr", "LineNumber"),
            InventoryId: RequiredString(source, "InventoryID"),
            Gtin: String(source, "GTIN", "Gtin", "Barcode"),
            Description: String(source, "TransactionDescr", "TransactionDescription", "Description") ?? string.Empty,
            Quantity: quantity,
            Uom: RequiredString(source, "UOM", "Uom"),
            PackSize: Decimal(source, "PackSize"),
            UnitAmountExcludingTax: unitExcludingTax,
            UnitAmountIncludingTax: unitIncludingTax,
            TaxAmount: unitTaxAmount,
            TaxCategoryCode: taxPercentage > 0m ? "STANDARD" : "ZERO",
            TaxPercentage: taxPercentage,
            IsCatchWeight: false);
    }

    private static InventoryCrossReference? MapInventoryCrossReference(JsonElement source)
    {
        var alternateId = String(source, "AlternateID", "AlternateId");
        if (!IsValidGtin(alternateId))
        {
            return null;
        }

        return new InventoryCrossReference(
            alternateId!,
            String(source, "AlternateType", "Type"),
            String(source, "UOM", "Uom"));
    }

    private static string? SelectGtin(
        IReadOnlyList<InventoryCrossReference> references,
        string invoiceUom)
    {
        var exactUom = references
            .Where(reference => string.Equals(reference.Uom, invoiceUom, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var global = references
            .Where(reference => string.IsNullOrWhiteSpace(reference.Uom))
            .ToArray();

        return SingleDistinctPreferred(exactUom)
            ?? SingleDistinctPreferred(global)
            ?? SingleDistinctPreferred(references);
    }

    private static string? SingleDistinctPreferred(IReadOnlyCollection<InventoryCrossReference> references)
    {
        if (references.Count == 0)
        {
            return null;
        }

        var preferred = references
            .Where(reference => IsPreferredGtinType(reference.Type))
            .Select(reference => reference.AlternateId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (preferred.Length == 1)
        {
            return preferred[0];
        }

        var all = references
            .Select(reference => reference.AlternateId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return all.Length == 1 ? all[0] : null;
    }

    private static bool IsPreferredGtinType(string? value)
        => value is not null
            && (value.Contains("global", StringComparison.OrdinalIgnoreCase)
                || value.Contains("barcode", StringComparison.OrdinalIgnoreCase)
                || value.Contains("gtin", StringComparison.OrdinalIgnoreCase)
                || value.Contains("ean", StringComparison.OrdinalIgnoreCase)
                || value.Contains("upc", StringComparison.OrdinalIgnoreCase));

    private static bool IsValidGtin(string? value)
    {
        if (value is null
            || value.Length is not (8 or 12 or 13 or 14)
            || value.Any(character => character is < '0' or > '9'))
        {
            return false;
        }

        var sum = 0;
        for (var index = value.Length - 2; index >= 0; index--)
        {
            var digit = value[index] - '0';
            var positionFromRight = value.Length - 1 - index;
            sum += digit * (positionFromRight % 2 == 1 ? 3 : 1);
        }

        var expectedCheckDigit = (10 - sum % 10) % 10;
        return expectedCheckDigit == value[^1] - '0';
    }

    private static T AssertSingle<T>(IReadOnlyList<T> values)
        => values.Count == 1
            ? values[0]
            : throw new InvalidOperationException("Expected one Acumatica invoice.");

    private static decimal LineAmount(JsonElement source)
    {
        var quantity = RequiredDecimal(source, "Qty", "Quantity");
        return Decimal(source, "Amount", "ExtendedPrice") ??
            (Decimal(source, "UnitPrice") ?? 0m) * quantity;
    }

    private static TaxDetail MapTaxDetail(JsonElement source)
    {
        var taxableAmount = Decimal(source, "TaxableAmount") ?? 0m;
        var taxAmount = Decimal(source, "TaxAmount") ?? 0m;
        var percentage = taxableAmount == 0m
            ? 0m
            : decimal.Round(taxAmount / taxableAmount * 100m, 2, MidpointRounding.AwayFromZero);
        return new TaxDetail(String(source, "TaxID"), percentage);
    }

    private static decimal TaxRate(
        JsonElement line,
        IReadOnlyList<TaxDetail> taxDetails,
        decimal totalExcludingTax,
        decimal totalTax)
    {
        var taxCategory = String(line, "TaxCategory", "TaxCategoryID");
        var exact = taxDetails.FirstOrDefault(detail =>
            string.Equals(detail.TaxId, taxCategory, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact.Percentage;
        }

        if (taxDetails.Count == 1)
        {
            return taxDetails[0].Percentage;
        }

        return totalExcludingTax == 0m
            ? 0m
            : decimal.Round(totalTax / totalExcludingTax * 100m, 2, MidpointRounding.AwayFromZero);
    }

    private static decimal[] AllocateCurrency(decimal total, IReadOnlyList<decimal> weights)
    {
        if (weights.Count == 0)
        {
            return [];
        }

        var roundedTotal = RoundCurrency(total);
        var sign = Math.Sign(roundedTotal);
        var absoluteTotal = Math.Abs(roundedTotal);
        var positiveWeights = weights.Select(weight => Math.Max(weight, 0m)).ToArray();
        var weightTotal = positiveWeights.Sum();
        if (weightTotal == 0m)
        {
            positiveWeights = Enumerable.Repeat(1m, weights.Count).ToArray();
            weightTotal = weights.Count;
        }

        var rawAllocations = positiveWeights
            .Select(weight => absoluteTotal * weight / weightTotal)
            .ToArray();
        var allocations = rawAllocations
            .Select(value => decimal.Floor(value * 100m) / 100m)
            .ToArray();
        var remainingCents = decimal.ToInt32(
            decimal.Round((absoluteTotal - allocations.Sum()) * 100m, 0));
        var remainderOrder = rawAllocations
            .Select((value, index) => new
            {
                Index = index,
                Remainder = value - allocations[index]
            })
            .OrderByDescending(item => item.Remainder)
            .ThenBy(item => item.Index)
            .ToArray();

        for (var cent = 0; cent < remainingCents; cent++)
        {
            allocations[remainderOrder[cent % remainderOrder.Length].Index] += 0.01m;
        }

        return allocations.Select(value => value * sign).ToArray();
    }

    private static decimal RoundCurrency(decimal value)
        => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static async Task<JsonElement[]> ReadArrayAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Acumatica {operation} returned an unexpected payload shape.");
        }

        return document.RootElement.EnumerateArray().Select(element => element.Clone()).ToArray();
    }

    private static async Task<JsonElement> ReadObjectAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Acumatica {operation} returned an unexpected payload shape.");
        }

        return document.RootElement.Clone();
    }

    private static JsonElement[] Array(JsonElement source, string name)
        => source.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(element => element.Clone()).ToArray()
            : [];

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
        return DateTimeOffsetValue(source, names) ?? throw MissingField(names);
    }

    private static DateTimeOffset? DateTimeOffsetValue(JsonElement source, params string[] names)
        => DateTimeOffset.TryParse(String(source, names), out var result) ? result : null;

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

        if ((options.CustomerAccounts.Length == 0 && options.ParentCustomerAccounts.Length == 0) ||
            options.CustomerAccounts.Any(string.IsNullOrWhiteSpace) ||
            options.ParentCustomerAccounts.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                "Acumatica:CustomerAccounts or Acumatica:ParentCustomerAccounts must contain at least one account.");
        }

        if (options.InvoiceDateFrom is null)
        {
            throw new InvalidOperationException(
                "Acumatica:InvoiceDateFrom is required to prevent unbounded historical invoice ingestion.");
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

    private sealed record TaxDetail(string? TaxId, decimal Percentage);

    private sealed record InventoryCrossReference(string AlternateId, string? Type, string? Uom);
}
