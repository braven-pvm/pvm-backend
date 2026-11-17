using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pvm.Application.Documents;

namespace Pvm.Infrastructure.Acumatica;

/// <summary>
/// Finds the Acumatica invoices a proof of delivery points to and attaches filed PDFs (design 6.5
/// steps 1 and 2, and 6.6). All customers are in scope; only documents of the type "Invoice" are
/// candidates. Statuses come back as Acumatica gives them: <see cref="PodMatcher"/> leaves voided
/// and cancelled invoices out.
/// </summary>
/// <remarks>
/// Request shapes (all under <c>entity/{endpoint}/{version}/</c>, inside one signed-in session):
/// <list type="bullet">
/// <item>Sales-order and customer-order numbers (a shipment line counts only when its InvoiceType is "Invoice"): <c>GET SalesOrder?$select=OrderType,OrderNbr,CustomerOrder,Shipments/InvoiceType,Shipments/InvoiceNbr&amp;$expand=Shipments&amp;$filter=(OrderNbr eq 'x' or ...)</c>
/// (or <c>CustomerOrder eq 'x'</c>); the invoice numbers come from the order's shipment lines.</item>
/// <item>Invoices: <c>GET SalesInvoice?$select=Type,ReferenceNbr,Date,CustomerID,Status&amp;$filter=Type eq 'Invoice' and (ReferenceNbr eq 'x' or ...)</c>.</item>
/// <item>Customer names: <c>GET Customer?$select=CustomerID,CustomerName,ParentRecord&amp;$filter=(CustomerID eq 'x' or ...)</c>,
/// then the same for the parent customers.</item>
/// <item>One invoice by its exact reference: <c>GET SalesInvoice?$select=...&amp;$filter=Type eq 'Invoice' and ReferenceNbr eq 'x'</c>.</item>
/// <item>Attachment: <c>PUT SalesInvoice/Invoice/{ref}/files/{fileName}</c>.</item>
/// </list>
/// </remarks>
public sealed class AcumaticaDocumentClient(
    HttpClient httpClient,
    IOptions<AcumaticaOptions> options,
    ILogger<AcumaticaDocumentClient> logger) : IAcumaticaDocumentClient
{
    /// <summary>A defensive limit against a runaway extraction: distinct numbers looked up per kind.</summary>
    public const int MaxNumbersPerKind = 10;

    public const int SearchLimit = 20;

    private const int FilterChunkSize = 20;
    private const string InvoiceDocumentType = "Invoice";
    private const string SalesInvoiceEntity = "SalesInvoice";
    private const string InvoiceFields = "Type,ReferenceNbr,Date,CustomerID,Status";
    private const string CustomerFields = "CustomerID,CustomerName,ParentRecord";
    private const string OrderFields = "OrderType,OrderNbr,CustomerOrder,Shipments/InvoiceType,Shipments/InvoiceNbr";

    private readonly AcumaticaOptions _options = options.Value;
    private readonly AcumaticaRestSession _session = new(httpClient, options.Value, logger);

    public async Task<IReadOnlyList<CandidateHit>> FindInvoicesAsync(
        PodExtraction extraction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(extraction);

        // One printed number counts once, under the first kind in this order, so it can never
        // give an invoice two kinds of hit by itself.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var invoiceNumbers = Distinct(extraction.InvoiceNumbers, "invoice", seen);
        var salesOrderNumbers = Distinct(extraction.SalesOrderNumbers, "salesOrder", seen);
        var customerOrderNumbers = Distinct(extraction.CustomerOrderNumbers, "customerOrder", seen);
        if (invoiceNumbers.Count + salesOrderNumbers.Count + customerOrderNumbers.Count == 0)
        {
            return [];
        }

        AcumaticaRestSession.ValidateConnection(_options);
        return await _session.RunAsync<IReadOnlyList<CandidateHit>>(async sessionCookie =>
        {
            var kindsByReference = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            void Add(string reference, string kind)
            {
                if (!kindsByReference.TryGetValue(reference, out var kinds))
                {
                    kinds = [];
                    kindsByReference[reference] = kinds;
                }

                if (!kinds.Contains(kind, StringComparer.Ordinal))
                {
                    kinds.Add(kind);
                }
            }

            foreach (var number in invoiceNumbers)
            {
                Add(number, "invoice");
            }

            foreach (var reference in await InvoicesOfOrdersAsync("OrderNbr", salesOrderNumbers, sessionCookie, cancellationToken))
            {
                Add(reference, "salesOrder");
            }

            foreach (var reference in await InvoicesOfOrdersAsync("CustomerOrder", customerOrderNumbers, sessionCookie, cancellationToken))
            {
                Add(reference, "customerOrder");
            }

            if (kindsByReference.Count == 0)
            {
                return [];
            }

            var rows = await FetchInvoicesByReferenceAsync(kindsByReference.Keys.ToArray(), sessionCookie, cancellationToken);
            var candidates = await ToCandidatesAsync(rows, knownCustomers: new Dictionary<string, CustomerRow>(), sessionCookie, cancellationToken);
            return candidates
                .SelectMany(candidate => kindsByReference.TryGetValue(candidate.ReferenceNumber, out var kinds)
                    ? kinds.Select(kind => new CandidateHit(candidate, kind))
                    : [])
                .ToArray();
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<InvoiceCandidate>> SearchInvoicesAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var text = query?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        AcumaticaRestSession.ValidateConnection(_options);
        return await _session.RunAsync<IReadOnlyList<InvoiceCandidate>>(async sessionCookie =>
        {
            var escaped = AcumaticaRestSession.EscapeFilterValue(text);
            var namedCustomers = await FetchCustomersAsync(
                $"substringof('{escaped}',CustomerName)",
                SearchLimit,
                sessionCookie,
                cancellationToken);

            var customerFilter = string.Concat(namedCustomers.Keys
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(id => $" or CustomerID eq '{AcumaticaRestSession.EscapeFilterValue(id)}'"));
            var filter = $"Type eq '{InvoiceDocumentType}' and (startswith(ReferenceNbr,'{escaped}'){customerFilter})";
            var orderBy = Uri.EscapeDataString("Date desc,ReferenceNbr desc");
            var uri = _session.BuildUri(
                _session.BuildEntityEndpoint(SalesInvoiceEntity)
                + $"?$select={Uri.EscapeDataString(InvoiceFields)}&$filter={Uri.EscapeDataString(filter)}&$orderby={orderBy}&$top={SearchLimit}");
            var rows = await GetArrayAsync(uri, "invoice search", sessionCookie, cancellationToken);
            return await ToCandidatesAsync(
                rows.Select(MapInvoiceRow)
                    .OfType<InvoiceRow>()
                    .Where(row => !InvoiceStatuses.IsOutOfScope(row.Status))
                    .ToArray(),
                namedCustomers,
                sessionCookie,
                cancellationToken);
        }, cancellationToken);
    }

    public async Task<InvoiceCandidate?> GetInvoiceAsync(
        string referenceNumber,
        CancellationToken cancellationToken)
    {
        var reference = referenceNumber?.Trim();
        if (string.IsNullOrEmpty(reference))
        {
            return null;
        }

        AcumaticaRestSession.ValidateConnection(_options);
        return await _session.RunAsync<InvoiceCandidate?>(async sessionCookie =>
        {
            var filter = $"Type eq '{InvoiceDocumentType}' and ReferenceNbr eq '{AcumaticaRestSession.EscapeFilterValue(reference)}'";
            var uri = _session.BuildUri(
                _session.BuildEntityEndpoint(SalesInvoiceEntity)
                + $"?$select={Uri.EscapeDataString(InvoiceFields)}&$filter={Uri.EscapeDataString(filter)}&$top=2");
            var rows = (await GetArrayAsync(uri, "invoice lookup", sessionCookie, cancellationToken))
                .Select(MapInvoiceRow)
                .OfType<InvoiceRow>()
                .Where(row => string.Equals(row.ReferenceNumber, reference, StringComparison.OrdinalIgnoreCase)
                    && !InvoiceStatuses.IsOutOfScope(row.Status))
                .Take(1)
                .ToArray();
            var candidates = await ToCandidatesAsync(rows, knownCustomers: new Dictionary<string, CustomerRow>(), sessionCookie, cancellationToken);
            return candidates.Count == 0 ? null : candidates[0];
        }, cancellationToken);
    }

    public async Task AttachFileAsync(
        string entity,
        string referenceNumber,
        string fileName,
        Stream pdf,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(entity, SalesInvoiceEntity, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Only {SalesInvoiceEntity} attachments are supported, not '{entity}'.",
                nameof(entity));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(referenceNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(pdf);
        AcumaticaRestSession.ValidateConnection(_options);

        var uri = _session.BuildUri(
            _session.BuildEntityEndpoint(SalesInvoiceEntity)
            + $"/{InvoiceDocumentType}/{Uri.EscapeDataString(referenceNumber.Trim())}"
            + $"/files/{Uri.EscapeDataString(fileName)}");
        await _session.RunAsync(async sessionCookie =>
        {
            using var request = AcumaticaRestSession.CreateRequest(HttpMethod.Put, uri, sessionCookie);
            request.Content = new StreamContent(pdf);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var response = await httpClient.SendAsync(request, cancellationToken);
            AcumaticaRestSession.EnsureSuccess(response, "file attachment");
            return true;
        }, cancellationToken);
    }

    private List<string> Distinct(IReadOnlyList<string>? values, string kind, HashSet<string> seen)
    {
        var result = new List<string>();
        var over = 0;
        foreach (var value in values ?? [])
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var number = value.Trim();
            if (!seen.Add(number))
            {
                continue;
            }

            if (result.Count == MaxNumbersPerKind)
            {
                over++;
                continue;
            }

            result.Add(number);
        }

        if (over > 0)
        {
            logger.LogWarning(
                "The extraction listed {Count} distinct {Kind} numbers; only the first {Limit} are looked up in Acumatica.",
                result.Count + over,
                kind,
                MaxNumbersPerKind);
        }

        return result;
    }

    /// <summary>The invoice numbers on the shipment lines of the sales orders whose field matches.</summary>
    private async Task<IReadOnlyList<string>> InvoicesOfOrdersAsync(
        string field,
        IReadOnlyList<string> numbers,
        string? sessionCookie,
        CancellationToken cancellationToken)
    {
        if (numbers.Count == 0)
        {
            return [];
        }

        var filter = "(" + string.Join(
            " or ",
            numbers.Select(number => $"{field} eq '{AcumaticaRestSession.EscapeFilterValue(number)}'")) + ")";
        var uri = _session.BuildUri(
            _session.BuildEntityEndpoint("SalesOrder")
            + $"?$select={Uri.EscapeDataString(OrderFields)}&$expand=Shipments&$filter={Uri.EscapeDataString(filter)}&$top={_options.PageSize}");
        var orders = await GetArrayAsync(uri, "sales order lookup", sessionCookie, cancellationToken);
        if (orders.Length >= _options.PageSize)
        {
            logger.LogWarning(
                "The Acumatica sales order lookup by {Field} returned a full page of {PageSize}; later orders are not looked at.",
                field,
                _options.PageSize);
        }

        var references = new List<string>();
        foreach (var order in orders)
        {
            if (!order.TryGetProperty("Shipments", out var shipments) || shipments.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var shipment in shipments.EnumerateArray())
            {
                var invoiceNumber = Text(shipment, "InvoiceNbr")?.Trim();
                var invoiceType = Text(shipment, "InvoiceType");
                // Fail closed: a shipment line counts only when it says it was invoiced as an Invoice.
                if (string.IsNullOrEmpty(invoiceNumber) ||
                    !string.Equals(invoiceType?.Trim(), InvoiceDocumentType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!references.Contains(invoiceNumber, StringComparer.OrdinalIgnoreCase))
                {
                    references.Add(invoiceNumber);
                }
            }
        }

        return references;
    }

    private async Task<IReadOnlyList<InvoiceRow>> FetchInvoicesByReferenceAsync(
        IReadOnlyList<string> references,
        string? sessionCookie,
        CancellationToken cancellationToken)
    {
        var rows = new List<InvoiceRow>();
        foreach (var chunk in references.Chunk(FilterChunkSize))
        {
            var referenceFilter = string.Join(
                " or ",
                chunk.Select(reference => $"ReferenceNbr eq '{AcumaticaRestSession.EscapeFilterValue(reference)}'"));
            var filter = $"Type eq '{InvoiceDocumentType}' and ({referenceFilter})";
            var uri = _session.BuildUri(
                _session.BuildEntityEndpoint(SalesInvoiceEntity)
                + $"?$select={Uri.EscapeDataString(InvoiceFields)}&$filter={Uri.EscapeDataString(filter)}&$top={chunk.Length}");
            var page = await GetArrayAsync(uri, "invoice lookup", sessionCookie, cancellationToken);
            rows.AddRange(page.Select(MapInvoiceRow).OfType<InvoiceRow>());
        }

        return rows.DistinctBy(row => row.ReferenceNumber, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task<IReadOnlyList<InvoiceCandidate>> ToCandidatesAsync(
        IReadOnlyList<InvoiceRow> rows,
        Dictionary<string, CustomerRow> knownCustomers,
        string? sessionCookie,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var customers = new Dictionary<string, CustomerRow>(knownCustomers, StringComparer.OrdinalIgnoreCase);
        await AddCustomersAsync(customers, rows.Select(row => row.CustomerId), sessionCookie, cancellationToken);
        await AddCustomersAsync(
            customers,
            rows.Select(row => customers.GetValueOrDefault(row.CustomerId)?.ParentId).OfType<string>(),
            sessionCookie,
            cancellationToken);

        return rows.Select(row =>
        {
            var customer = customers.GetValueOrDefault(row.CustomerId);
            var parent = customer?.ParentId is { } parentId ? customers.GetValueOrDefault(parentId) : null;
            return new InvoiceCandidate(
                row.ReferenceNumber,
                row.Date,
                row.CustomerId,
                customer?.Name ?? string.Empty,
                parent?.Name,
                ShipToName: null,
                row.Status);
        }).ToArray();
    }

    private async Task AddCustomersAsync(
        Dictionary<string, CustomerRow> customers,
        IEnumerable<string> ids,
        string? sessionCookie,
        CancellationToken cancellationToken)
    {
        var missing = ids
            .Where(id => !string.IsNullOrWhiteSpace(id) && !customers.ContainsKey(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var chunk in missing.Chunk(FilterChunkSize))
        {
            var filter = "(" + string.Join(
                " or ",
                chunk.Select(id => $"CustomerID eq '{AcumaticaRestSession.EscapeFilterValue(id)}'")) + ")";
            var found = await FetchCustomersAsync(filter, chunk.Length, sessionCookie, cancellationToken);
            foreach (var id in chunk)
            {
                if (found.TryGetValue(id, out var customer))
                {
                    customers[id] = customer;
                }
            }
        }
    }

    private async Task<Dictionary<string, CustomerRow>> FetchCustomersAsync(
        string filter,
        int top,
        string? sessionCookie,
        CancellationToken cancellationToken)
    {
        var uri = _session.BuildUri(
            _session.BuildEntityEndpoint("Customer")
            + $"?$select={Uri.EscapeDataString(CustomerFields)}&$filter={Uri.EscapeDataString(filter)}&$top={top}");
        var page = await GetArrayAsync(uri, "customer lookup", sessionCookie, cancellationToken);
        var customers = new Dictionary<string, CustomerRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in page)
        {
            var id = Text(source, "CustomerID")?.Trim();
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var parentId = Text(source, "ParentRecord")?.Trim();
            customers[id] = new CustomerRow(
                Text(source, "CustomerName")?.Trim() ?? string.Empty,
                string.IsNullOrEmpty(parentId) ? null : parentId);
        }

        return customers;
    }

    private async Task<JsonElement[]> GetArrayAsync(
        Uri uri,
        string operation,
        string? sessionCookie,
        CancellationToken cancellationToken)
    {
        using var request = AcumaticaRestSession.CreateRequest(HttpMethod.Get, uri, sessionCookie);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        AcumaticaRestSession.EnsureSuccess(response, operation);
        return await AcumaticaRestSession.ReadArrayAsync(response, operation, cancellationToken);
    }

    /// <summary>An invoice row, or null when Acumatica returned a document of another type.</summary>
    private static InvoiceRow? MapInvoiceRow(JsonElement source)
    {
        var type = Text(source, "Type");
        if (type is not null && !string.Equals(type.Trim(), InvoiceDocumentType, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var reference = Required(source, "ReferenceNbr");
        var dateText = Required(source, "Date");
        if (!DateTimeOffset.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new InvalidOperationException($"Acumatica invoice {reference} has an unreadable date.");
        }

        return new InvoiceRow(
            reference.Trim(),
            DateOnly.FromDateTime(date.DateTime),
            Required(source, "CustomerID").Trim(),
            Required(source, "Status").Trim());
    }

    private static string Required(JsonElement source, string name)
        => Text(source, name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Acumatica invoice payload is missing required field {name}.");

    /// <summary>A field's text, unwrapping Acumatica's <c>{ "value": ... }</c> envelope.</summary>
    private static string? Text(JsonElement source, string name)
    {
        if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty(name, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Object && property.TryGetProperty("value", out var wrapped))
        {
            property = wrapped;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    private sealed record InvoiceRow(string ReferenceNumber, DateOnly Date, string CustomerId, string Status);

    private sealed record CustomerRow(string Name, string? ParentId);
}
