using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pvm.Application.Documents;
using Pvm.Infrastructure.Acumatica;

namespace Pvm.Infrastructure.Tests.Acumatica;

public sealed class AcumaticaDocumentClientTests
{
    private const string Customers = """
        [
          { "CustomerID": { "value": "C100" }, "CustomerName": { "value": "Shoprite Mthatha" }, "ParentRecord": { "value": "P1" } },
          { "CustomerID": { "value": "C200" }, "CustomerName": { "value": "OK Foods" } }
        ]
        """;

    private const string Parents = """
        [ { "CustomerID": { "value": "P1" }, "CustomerName": { "value": "Shoprite Checkers (Pty) Ltd" } } ]
        """;

    [Fact]
    public async Task FindInvoices_ByInvoiceNumber_FiltersTypeInvoice()
    {
        var handler = new RoutingHandler(request => Route(request, salesInvoices: Invoice("INV001", "C100", "Open")));
        var client = CreateClient(handler);

        var hits = await client.FindInvoicesAsync(Extraction(invoices: ["INV001"]), CancellationToken.None);

        var hit = Assert.Single(hits);
        Assert.Equal("invoice", hit.MatchedOn);
        Assert.Equal(
            new InvoiceCandidate("INV001", new DateOnly(2026, 11, 10), "C100", "Shoprite Mthatha", "Shoprite Checkers (Pty) Ltd", null, "Open"),
            hit.Invoice);

        var invoiceRequest = Assert.Single(handler.Requests, request => request.Path.EndsWith("/SalesInvoice", StringComparison.Ordinal));
        Assert.Equal(HttpMethod.Get, invoiceRequest.Method);
        Assert.Equal("/PVMGroup25R1/entity/Default/24.200.001/SalesInvoice", invoiceRequest.Path);
        Assert.Contains("$filter=Type eq 'Invoice' and (ReferenceNbr eq 'INV001')", invoiceRequest.Query);
        Assert.DoesNotContain("CustomerID", invoiceRequest.Filter);
        Assert.Contains("$select=Type,ReferenceNbr,Date,CustomerID,Status", invoiceRequest.Query);
        Assert.DoesNotContain(handler.Requests, request => request.Path.EndsWith("/SalesOrder", StringComparison.Ordinal));
        Assert.Equal("ASP.NET_SessionId=abc", invoiceRequest.Cookie);
        Assert.EndsWith("/entity/auth/logout", handler.Requests[^1].Path);
    }

    [Fact]
    public async Task FindInvoices_ByCustomerOrder_LooksUpSalesOrdersThenInvoices()
    {
        const string salesOrders = """
            [{
              "OrderType": { "value": "SO" },
              "OrderNbr": { "value": "SO100" },
              "CustomerOrder": { "value": "PO-77" },
              "Shipments": [
                { "InvoiceType": { "value": "Invoice" }, "InvoiceNbr": { "value": "INV500" } },
                { "InvoiceType": { "value": "Credit Memo" }, "InvoiceNbr": { "value": "CM001" } },
                { "InvoiceType": { "value": "Invoice" }, "InvoiceNbr": null }
              ]
            }]
            """;
        var handler = new RoutingHandler(request => Route(
            request,
            salesOrders: salesOrders,
            salesInvoices: Invoice("INV500", "C200", "Closed")));
        var client = CreateClient(handler);

        var hits = await client.FindInvoicesAsync(Extraction(customerOrders: ["PO-77"]), CancellationToken.None);

        var hit = Assert.Single(hits);
        Assert.Equal("customerOrder", hit.MatchedOn);
        Assert.Equal("INV500", hit.Invoice.ReferenceNumber);
        Assert.Equal("OK Foods", hit.Invoice.CustomerName);
        Assert.Null(hit.Invoice.ParentCustomerName);

        var paths = handler.Requests.Select(request => request.Path[(request.Path.LastIndexOf('/') + 1)..]).ToArray();
        Assert.Equal(["login", "SalesOrder", "SalesInvoice", "Customer", "logout"], paths);

        var orderRequest = handler.Requests[1];
        Assert.Contains("$filter=(CustomerOrder eq 'PO-77')", orderRequest.Query);
        Assert.Contains("$expand=Shipments", orderRequest.Query);
        Assert.Contains("$select=OrderType,OrderNbr,CustomerOrder,Shipments/InvoiceType,Shipments/InvoiceNbr", orderRequest.Query);

        var invoiceRequest = handler.Requests[2];
        Assert.Contains("Type eq 'Invoice' and (ReferenceNbr eq 'INV500')", invoiceRequest.Filter);
        Assert.DoesNotContain("CM001", invoiceRequest.Filter);
    }

    [Fact]
    public async Task FindInvoices_BySalesOrder_FiltersOrderNumber()
    {
        const string salesOrders = """
            [{ "OrderType": { "value": "SO" }, "OrderNbr": { "value": "SO100" },
               "Shipments": [ { "InvoiceType": { "value": "Invoice" }, "InvoiceNbr": { "value": "INV500" } } ] }]
            """;
        var handler = new RoutingHandler(request => Route(
            request,
            salesOrders: salesOrders,
            salesInvoices: Invoice("INV500", "C200", "Open")));
        var client = CreateClient(handler);

        var hits = await client.FindInvoicesAsync(Extraction(salesOrders: ["SO100"]), CancellationToken.None);

        Assert.Equal("salesOrder", Assert.Single(hits).MatchedOn);
        var orderRequest = Assert.Single(handler.Requests, request => request.Path.EndsWith("/SalesOrder", StringComparison.Ordinal));
        Assert.Contains("$filter=(OrderNbr eq 'SO100')", orderRequest.Query);
    }

    [Fact]
    public async Task FindInvoices_DeduplicatesTheSameInvoiceFromTwoNumbers()
    {
        const string salesOrders = """
            [{
              "OrderType": { "value": "SO" },
              "OrderNbr": { "value": "SO100" },
              "Shipments": [
                { "InvoiceType": { "value": "Invoice" }, "InvoiceNbr": { "value": "INV500" } },
                { "InvoiceType": { "value": "Invoice" }, "InvoiceNbr": { "value": "INV500" } }
              ]
            }]
            """;
        var handler = new RoutingHandler(request => Route(
            request,
            salesOrders: salesOrders,
            salesInvoices: Invoice("INV500", "C200", "Open")));
        var client = CreateClient(handler);

        var hits = await client.FindInvoicesAsync(
            Extraction(invoices: ["INV500", "inv500 "], salesOrders: ["SO100"]),
            CancellationToken.None);

        Assert.Equal(2, hits.Count);
        Assert.All(hits, hit => Assert.Equal("INV500", hit.Invoice.ReferenceNumber));
        Assert.Equal(["invoice", "salesOrder"], hits.Select(hit => hit.MatchedOn).Order(StringComparer.Ordinal).ToArray());
        var invoiceRequest = Assert.Single(handler.Requests, request => request.Path.EndsWith("/SalesInvoice", StringComparison.Ordinal));
        Assert.Contains("(ReferenceNbr eq 'INV500')", invoiceRequest.Filter);
    }

    [Fact]
    public async Task FindInvoices_SameStringUnderTwoKinds_CountsOnce()
    {
        var handler = new RoutingHandler(request => Route(request, salesInvoices: Invoice("12345", "C200", "Open")));
        var client = CreateClient(handler);

        var hits = await client.FindInvoicesAsync(
            Extraction(invoices: ["12345"], salesOrders: ["12345 "], customerOrders: [" 12345"]),
            CancellationToken.None);

        var hit = Assert.Single(hits);
        Assert.Equal("invoice", hit.MatchedOn);
        Assert.DoesNotContain(handler.Requests, request => request.Path.EndsWith("/SalesOrder", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FindInvoices_IgnoresBlankNumbersAndCapsEachListAtTen()
    {
        var handler = new RoutingHandler(request => Route(request, salesInvoices: "[]"));
        var logger = new ListLogger();
        var client = CreateClient(handler, logger);
        var numbers = Enumerable.Range(1, 12).Select(index => $"INV{index:000}").Concat([" ", ""]).ToArray();

        var hits = await client.FindInvoicesAsync(Extraction(invoices: numbers), CancellationToken.None);

        Assert.Empty(hits);
        var invoiceRequest = Assert.Single(handler.Requests, request => request.Path.EndsWith("/SalesInvoice", StringComparison.Ordinal));
        Assert.Contains("ReferenceNbr eq 'INV010'", invoiceRequest.Filter);
        Assert.DoesNotContain("INV011", invoiceRequest.Filter);
        Assert.DoesNotContain("ReferenceNbr eq ''", invoiceRequest.Filter);
        Assert.DoesNotContain(handler.Requests, request => request.Path.EndsWith("/Customer", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("invoice", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FindInvoices_NoNumbers_DoesNotSignIn()
    {
        var handler = new RoutingHandler(request => Route(request));
        var client = CreateClient(handler);

        var hits = await client.FindInvoicesAsync(Extraction(invoices: [" "]), CancellationToken.None);

        Assert.Empty(hits);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FindInvoices_EscapesQuotesInNumbers()
    {
        var handler = new RoutingHandler(request => Route(request, salesInvoices: "[]"));
        var client = CreateClient(handler);

        await client.FindInvoicesAsync(Extraction(invoices: ["O'NEIL-1"]), CancellationToken.None);

        var invoiceRequest = Assert.Single(handler.Requests, request => request.Path.EndsWith("/SalesInvoice", StringComparison.Ordinal));
        Assert.Contains("ReferenceNbr eq 'O''NEIL-1'", invoiceRequest.Filter);
    }

    [Fact]
    public async Task SearchInvoices_MatchesReferenceOrCustomerName_NewestFirst()
    {
        const string nameMatches = """
            [ { "CustomerID": { "value": "C200" }, "CustomerName": { "value": "OK Foods" } } ]
            """;
        const string invoices = """
            [
              { "Type": { "value": "Invoice" }, "ReferenceNbr": { "value": "INV900" }, "Date": { "value": "2026-11-12T00:00:00+02:00" },
                "CustomerID": { "value": "C200" }, "Status": { "value": "Open" } },
              { "Type": { "value": "Invoice" }, "ReferenceNbr": { "value": "OK1" }, "Date": { "value": "2026-11-01T00:00:00+02:00" },
                "CustomerID": { "value": "C100" }, "Status": { "value": "Closed" } }
            ]
            """;
        var handler = new RoutingHandler(request =>
        {
            if (request.Path.EndsWith("/Customer", StringComparison.Ordinal) && request.Filter.Contains("substringof", StringComparison.Ordinal))
            {
                return Json(nameMatches);
            }

            return Route(request, salesInvoices: invoices);
        });
        var client = CreateClient(handler);

        var results = await client.SearchInvoicesAsync(" ok ", CancellationToken.None);

        Assert.Equal(["INV900", "OK1"], results.Select(invoice => invoice.ReferenceNumber).ToArray());
        Assert.Equal("OK Foods", results[0].CustomerName);
        Assert.Equal("Shoprite Mthatha", results[1].CustomerName);

        var nameRequest = handler.Requests.First(request => request.Path.EndsWith("/Customer", StringComparison.Ordinal));
        Assert.Contains("$filter=substringof('ok',CustomerName)", nameRequest.Query);
        Assert.Contains("$top=20", nameRequest.Query);

        var invoiceRequest = Assert.Single(handler.Requests, request => request.Path.EndsWith("/SalesInvoice", StringComparison.Ordinal));
        Assert.Contains("$filter=Type eq 'Invoice' and (startswith(ReferenceNbr,'ok') or CustomerID eq 'C200')", invoiceRequest.Query);
        Assert.Contains("$orderby=Date desc,ReferenceNbr desc", invoiceRequest.Query);
        Assert.Contains("$top=20", invoiceRequest.Query);
    }

    [Fact]
    public async Task SearchInvoices_BlankQuery_ReturnsNothing()
    {
        var handler = new RoutingHandler(request => Route(request));
        var client = CreateClient(handler);

        Assert.Empty(await client.SearchInvoicesAsync("  ", CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AttachFile_PutsToSalesInvoiceFilesEndpoint()
    {
        var handler = new RoutingHandler(request => Route(request));
        var client = CreateClient(handler);
        using var pdf = new MemoryStream("%PDF-1.7 test"u8.ToArray());

        await client.AttachFileAsync("SalesInvoice", "INV001", "POD-INV001-0f8e.pdf", pdf, CancellationToken.None);

        var put = Assert.Single(handler.Requests, request => request.Method == HttpMethod.Put);
        Assert.Equal("/PVMGroup25R1/entity/Default/24.200.001/SalesInvoice/Invoice/INV001/files/POD-INV001-0f8e.pdf", put.Path);
        Assert.Equal("%PDF-1.7 test", put.Body);
        Assert.Equal("ASP.NET_SessionId=abc", put.Cookie);
        Assert.EndsWith("/entity/auth/logout", handler.Requests[^1].Path);
    }

    [Fact]
    public async Task AttachFile_ServerError_Throws()
    {
        var handler = new RoutingHandler(request => request.Method == HttpMethod.Put
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : Route(request));
        var client = CreateClient(handler);
        using var pdf = new MemoryStream([1, 2, 3]);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.AttachFileAsync("SalesInvoice", "INV001", "POD-INV001-0f8e.pdf", pdf, CancellationToken.None));

        Assert.Contains("500", error.Message);
        Assert.EndsWith("/entity/auth/logout", handler.Requests[^1].Path);
    }

    [Fact]
    public async Task AttachFile_OtherEntity_IsRefused()
    {
        var handler = new RoutingHandler(request => Route(request));
        var client = CreateClient(handler);
        using var pdf = new MemoryStream([1]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.AttachFileAsync("SalesOrder", "SO1", "x.pdf", pdf, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    private static AcumaticaDocumentClient CreateClient(RoutingHandler handler, ListLogger? logger = null)
        => new(
            new HttpClient(handler),
            Options.Create(new AcumaticaOptions
            {
                BaseUrl = "https://acumatica.example/PVMGroup25R1",
                Company = "PVM Group",
                Username = "integration-user",
                Password = "secret",
                EndpointName = "Default",
                EndpointVersion = "24.200.001",
            }),
            logger ?? new ListLogger());

    private static PodExtraction Extraction(
        string[]? invoices = null,
        string[]? salesOrders = null,
        string[]? customerOrders = null)
        => new(invoices ?? [], salesOrders ?? [], customerOrders ?? [], null, null, true, null);

    private static string Invoice(string reference, string customerId, string status)
        => $$"""
            [{ "Type": { "value": "Invoice" }, "ReferenceNbr": { "value": "{{reference}}" },
               "Date": { "value": "2026-11-10T00:00:00+02:00" }, "CustomerID": { "value": "{{customerId}}" },
               "Status": { "value": "{{status}}" } }]
            """;

    private static HttpResponseMessage Route(
        CapturedRequest request,
        string? salesOrders = null,
        string? salesInvoices = null)
    {
        if (request.Path.EndsWith("/entity/auth/login", StringComparison.Ordinal))
        {
            var response = new HttpResponseMessage(HttpStatusCode.NoContent);
            response.Headers.Add("Set-Cookie", "ASP.NET_SessionId=abc; path=/; secure; HttpOnly");
            return response;
        }

        if (request.Path.EndsWith("/entity/auth/logout", StringComparison.Ordinal) || request.Method == HttpMethod.Put)
        {
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        if (request.Path.EndsWith("/SalesOrder", StringComparison.Ordinal))
        {
            return Json(salesOrders ?? "[]");
        }

        if (request.Path.EndsWith("/SalesInvoice", StringComparison.Ordinal))
        {
            return Json(salesInvoices ?? "[]");
        }

        if (request.Path.EndsWith("/Customer", StringComparison.Ordinal))
        {
            return Json(request.Filter.Contains("'P1'", StringComparison.Ordinal) ? Parents : Customers);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };

    private sealed class RoutingHandler(Func<CapturedRequest, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var cookie = request.Headers.TryGetValues("Cookie", out var cookies)
                ? string.Join("; ", cookies)
                : null;
            var captured = new CapturedRequest(request.Method, request.RequestUri!, body, cookie);
            Requests.Add(captured);
            return respond(captured);
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? Body, string? Cookie)
    {
        public string Path => Uri.AbsolutePath;

        public string Query => Uri.UnescapeDataString(Uri.Query);

        public string Filter
        {
            get
            {
                var start = Query.IndexOf("$filter=", StringComparison.Ordinal);
                if (start < 0)
                {
                    return string.Empty;
                }

                var end = Query.IndexOf("&$", start + 1, StringComparison.Ordinal);
                return end < 0 ? Query[start..] : Query[start..end];
            }
        }
    }

    private sealed class ListLogger : ILogger<AcumaticaDocumentClient>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
