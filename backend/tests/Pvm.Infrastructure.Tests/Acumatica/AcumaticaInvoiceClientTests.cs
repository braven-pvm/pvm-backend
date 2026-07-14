using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pvm.Infrastructure.Acumatica;

namespace Pvm.Infrastructure.Tests.Acumatica;

public sealed class AcumaticaInvoiceClientTests
{
    [Fact]
    public async Task FetchFinalizedInvoicesAsync_AuthenticatesWithConfiguredCompanyAndBranch()
    {
        using var handler = new SequenceHandler(
            _ => new HttpResponseMessage(HttpStatusCode.NoContent),
            _ => JsonResponse("[]"),
            _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var httpClient = new HttpClient(handler);
        var client = new AcumaticaInvoiceClient(httpClient, Options.Create(DefaultOptions()));

        await client.FetchFinalizedInvoicesAsync(CancellationToken.None);

        var login = Assert.Single(
            handler.Requests,
            request => request.Uri.AbsolutePath.EndsWith("/entity/auth/login", StringComparison.Ordinal));
        Assert.Equal(HttpMethod.Post, login.Method);

        using var body = JsonDocument.Parse(login.Body!);
        Assert.Equal("integration-user", body.RootElement.GetProperty("name").GetString());
        Assert.Equal("PVM Group", body.RootElement.GetProperty("company").GetString());
        Assert.Equal("MAIN", body.RootElement.GetProperty("branch").GetString());
    }

    [Fact]
    public async Task FetchFinalizedInvoicesAsync_MapsReleasedSalesInvoiceAndDetails()
    {
        const string salesInvoiceListJson = """
            [
              {
                "id": "c340d968-71a3-4ced-8c91-7cf1e653bec4",
                "ReferenceNbr": { "value": "INV000123" },
                "Type": { "value": "Invoice" },
                "Status": { "value": "Open" },
                "CustomerID": { "value": "SHOPRITE" }
              }
            ]
            """;
        const string salesInvoiceJson = """
            {
              "id": "c340d968-71a3-4ced-8c91-7cf1e653bec4",
              "ReferenceNbr": { "value": "INV000123" },
              "Type": { "value": "Invoice" },
              "Status": { "value": "Open" },
              "CustomerID": { "value": "SHOPRITE" },
              "CustomerOrder": { "value": "1210297232" },
              "Currency": { "value": "ZAR" },
              "Date": { "value": "2026-07-14T00:00:00+02:00" },
              "Amount": { "value": 171.76 },
              "DetailTotal": { "value": 165.96 },
              "DiscountTotal": { "value": 16.60 },
              "TaxTotal": { "value": 22.40 },
              "Details": [
                {
                  "LineNbr": { "value": 1 },
                  "InventoryID": { "value": "PVM-ITEM-1" },
                  "Description": { "value": "Sales Account - HO" },
                  "TransactionDescr": { "value": "PVM test item" },
                  "Qty": { "value": 2.00 },
                  "UOM": { "value": "EA" },
                  "UnitPrice": { "value": 82.98 },
                  "Amount": { "value": 165.96 },
                  "TaxCategory": { "value": "STANDARD15" }
                }
              ],
              "TaxDetails": [
                {
                  "TaxID": { "value": "STANDARD15" },
                  "TaxableAmount": { "value": 149.36 },
                  "TaxAmount": { "value": 22.40 }
                }
              ]
            }
            """;
        using var handler = new SequenceHandler(
            _ => new HttpResponseMessage(HttpStatusCode.NoContent),
            _ => JsonResponse(salesInvoiceListJson),
            _ => JsonResponse(salesInvoiceJson),
            _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var httpClient = new HttpClient(handler);
        var client = new AcumaticaInvoiceClient(httpClient, Options.Create(DefaultOptions()));

        var invoices = await client.FetchFinalizedInvoicesAsync(CancellationToken.None);

        var invoice = Assert.Single(invoices);
        Assert.Equal("c340d968-71a3-4ced-8c91-7cf1e653bec4", invoice.Id);
        Assert.Equal("INV000123", invoice.InvoiceNumber);
        Assert.Equal("Open", invoice.Status);
        Assert.Equal("SHOPRITE", invoice.CustomerAccount);
        Assert.Null(invoice.CustomerLocation);
        Assert.Equal("1210297232", invoice.CustomerOrder);
        Assert.Equal("ZAR", invoice.CurrencyCode);
        Assert.Equal("ZA", invoice.CountryCode);
        Assert.Equal(149.36m, invoice.TotalExcludingTax);
        Assert.Equal(171.76m, invoice.TotalIncludingTax);
        Assert.Equal(22.40m, invoice.TotalTax);

        var line = Assert.Single(invoice.Lines);
        Assert.Equal(1, line.LineNumber);
        Assert.Equal("PVM-ITEM-1", line.InventoryId);
        Assert.Equal("PVM test item", line.Description);
        Assert.Equal(2.00m, line.Quantity);
        Assert.Equal("EA", line.Uom);
        Assert.Equal(74.68m, line.UnitAmountExcludingTax);
        Assert.Equal(85.88m, line.UnitAmountIncludingTax);
        Assert.Equal(11.20m, line.TaxAmount);
        Assert.Equal("STANDARD", line.TaxCategoryCode);
        Assert.Equal(15.00m, line.TaxPercentage);
    }

    [Fact]
    public async Task FetchFinalizedInvoicesAsync_DistributesCurrencyRoundingWithoutNegativeLines()
    {
        const string summaryJson = """
            [{
              "id":"rounding-invoice",
              "ReferenceNbr":{"value":"INV-ROUND"},
              "Type":{"value":"Invoice"},
              "Status":{"value":"Open"},
              "CustomerID":{"value":"SHOPRITE"}
            }]
            """;
        const string detailJson = """
            {
              "id":"rounding-invoice",
              "ReferenceNbr":{"value":"INV-ROUND"},
              "Type":{"value":"Invoice"},
              "Status":{"value":"Open"},
              "CustomerID":{"value":"SHOPRITE"},
              "CustomerOrder":{"value":"PO-ROUND"},
              "Currency":{"value":"ZAR"},
              "Date":{"value":"2026-07-14T00:00:00+02:00"},
              "Amount":{"value":0.02},
              "TaxTotal":{"value":0.00},
              "Details":[
                {"LineNbr":{"value":1},"InventoryID":{"value":"SKU-1"},"Qty":{"value":1},"UOM":{"value":"EA"},"Amount":{"value":1}},
                {"LineNbr":{"value":2},"InventoryID":{"value":"SKU-2"},"Qty":{"value":1},"UOM":{"value":"EA"},"Amount":{"value":1}},
                {"LineNbr":{"value":3},"InventoryID":{"value":"SKU-3"},"Qty":{"value":1},"UOM":{"value":"EA"},"Amount":{"value":1}}
              ],
              "TaxDetails":[]
            }
            """;
        using var handler = new SequenceHandler(
            _ => new HttpResponseMessage(HttpStatusCode.NoContent),
            _ => JsonResponse(summaryJson),
            _ => JsonResponse(detailJson),
            _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var httpClient = new HttpClient(handler);
        var client = new AcumaticaInvoiceClient(httpClient, Options.Create(DefaultOptions()));

        var invoice = Assert.Single(await client.FetchFinalizedInvoicesAsync(CancellationToken.None));

        Assert.Equal(0.02m, invoice.Lines.Sum(line => line.UnitAmountExcludingTax));
        Assert.All(invoice.Lines, line => Assert.True(line.UnitAmountExcludingTax >= 0m));
    }

    [Fact]
    public async Task FetchFinalizedInvoicesAsync_RequestsOnlyFinalizedConfiguredCustomerAccounts()
    {
        using var handler = new SequenceHandler(
            _ => new HttpResponseMessage(HttpStatusCode.NoContent),
            _ => JsonResponse("[]"),
            _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var httpClient = new HttpClient(handler);
        var options = DefaultOptions() with
        {
            CustomerAccounts = ["SHOPRITE", "CHECKERS"],
            PageSize = 75
        };
        var client = new AcumaticaInvoiceClient(httpClient, Options.Create(options));

        await client.FetchFinalizedInvoicesAsync(CancellationToken.None);

        var request = Assert.Single(
            handler.Requests,
            item => item.Uri.AbsolutePath.EndsWith("/SalesInvoice", StringComparison.Ordinal));
        var query = Uri.UnescapeDataString(request.Uri.Query);
        Assert.DoesNotContain("$expand", query);
        Assert.Contains("Type eq 'Invoice'", query);
        Assert.Contains("Status eq 'Open'", query);
        Assert.Contains("Status eq 'Closed'", query);
        Assert.Contains("CustomerID eq 'SHOPRITE'", query);
        Assert.Contains("CustomerID eq 'CHECKERS'", query);
        Assert.Contains("Date ge datetimeoffset'2026-07-01T00:00:00+00:00'", query);
        Assert.Contains("$top=75", query);
        Assert.Contains("$skip=0", query);
    }

    [Fact]
    public async Task FetchFinalizedInvoicesAsync_ResolvesConfiguredParentAccountsToChildren()
    {
        const string customersJson = """
            [{"id":"customer-1","CustomerID":{"value":"82917"}}]
            """;
        using var handler = new SequenceHandler(
            _ => new HttpResponseMessage(HttpStatusCode.NoContent),
            _ => JsonResponse(customersJson),
            _ => JsonResponse("[]"),
            _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var httpClient = new HttpClient(handler);
        var options = DefaultOptions() with
        {
            CustomerAccounts = [],
            ParentCustomerAccounts = ["DEB2062"]
        };
        var client = new AcumaticaInvoiceClient(httpClient, Options.Create(options));

        await client.FetchFinalizedInvoicesAsync(CancellationToken.None);

        var customerRequest = Assert.Single(
            handler.Requests,
            item => item.Uri.AbsolutePath.EndsWith("/Customer", StringComparison.Ordinal));
        Assert.Contains("ParentRecord eq 'DEB2062'", Uri.UnescapeDataString(customerRequest.Uri.Query));

        var invoiceRequest = Assert.Single(
            handler.Requests,
            item => item.Uri.AbsolutePath.EndsWith("/SalesInvoice", StringComparison.Ordinal));
        var invoiceQuery = Uri.UnescapeDataString(invoiceRequest.Uri.Query);
        Assert.Contains("CustomerID eq 'DEB2062'", invoiceQuery);
        Assert.Contains("CustomerID eq '82917'", invoiceQuery);
    }

    [Fact]
    public async Task FetchFinalizedInvoicesAsync_UsesLoginSessionForInvoiceRequestAndLogout()
    {
        using var handler = new SequenceHandler(
            _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.NoContent);
                response.Headers.TryAddWithoutValidation("Set-Cookie", ".ASPXAUTH=session-token; path=/; secure; HttpOnly");
                return response;
            },
            _ => JsonResponse("[]"),
            _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var httpClient = new HttpClient(handler);
        var client = new AcumaticaInvoiceClient(httpClient, Options.Create(DefaultOptions()));

        await client.FetchFinalizedInvoicesAsync(CancellationToken.None);

        var invoiceRequest = Assert.Single(
            handler.Requests,
            request => request.Uri.AbsolutePath.EndsWith("/SalesInvoice", StringComparison.Ordinal));
        var logoutRequest = Assert.Single(
            handler.Requests,
            request => request.Uri.AbsolutePath.EndsWith("/entity/auth/logout", StringComparison.Ordinal));
        Assert.Equal(".ASPXAUTH=session-token", invoiceRequest.Cookie);
        Assert.Equal(".ASPXAUTH=session-token", logoutRequest.Cookie);
    }

    [Fact]
    public async Task FetchFinalizedInvoicesAsync_ReadsSubsequentPagesUntilPageIsIncomplete()
    {
        const string invoiceJson = """
            [{
              "id":"invoice-1",
              "ReferenceNbr":{"value":"INV1"},
              "Type":{"value":"Invoice"},
              "Status":{"value":"Open"},
              "CustomerID":{"value":"SHOPRITE"},
              "CustomerOrder":{"value":"PO1"},
              "Currency":{"value":"ZAR"},
              "Date":{"value":"2026-07-14T00:00:00+02:00"},
              "Amount":{"value":0},
              "TaxTotal":{"value":0},
              "Details":[]
            }]
            """;
        using var handler = new SequenceHandler(
            _ => new HttpResponseMessage(HttpStatusCode.NoContent),
            _ => JsonResponse(invoiceJson),
            _ => JsonResponse(invoiceJson[1..^1]),
            _ => JsonResponse("[]"),
            _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var httpClient = new HttpClient(handler);
        var options = DefaultOptions() with { PageSize = 1 };
        var client = new AcumaticaInvoiceClient(httpClient, Options.Create(options));

        var invoices = await client.FetchFinalizedInvoicesAsync(CancellationToken.None);

        Assert.Single(invoices);
        var requests = handler.Requests
            .Where(request => request.Uri.AbsolutePath.EndsWith("/SalesInvoice", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, requests.Length);
        Assert.Contains("$skip=0", Uri.UnescapeDataString(requests[0].Uri.Query));
        Assert.Contains("$skip=1", Uri.UnescapeDataString(requests[1].Uri.Query));
    }

    [Fact]
    public async Task FetchFinalizedInvoicesAsync_RetrievalFailureIsSanitizedAndSessionIsClosed()
    {
        using var handler = new SequenceHandler(
            _ => new HttpResponseMessage(HttpStatusCode.NoContent),
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("sensitive Acumatica diagnostic payload")
            },
            _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var httpClient = new HttpClient(handler);
        var client = new AcumaticaInvoiceClient(httpClient, Options.Create(DefaultOptions()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.FetchFinalizedInvoicesAsync(CancellationToken.None));

        Assert.Contains("HTTP 500", exception.Message);
        Assert.DoesNotContain("sensitive Acumatica diagnostic payload", exception.Message);
        Assert.Contains(
            handler.Requests,
            request => request.Uri.AbsolutePath.EndsWith("/entity/auth/logout", StringComparison.Ordinal));
    }

    private static AcumaticaOptions DefaultOptions()
        => new()
        {
            BaseUrl = "https://acumatica.example/PVMGroup25R1",
            Company = "PVM Group",
            Branch = "MAIN",
            Username = "integration-user",
            Password = "secret",
            EndpointName = "Default",
            EndpointVersion = "24.200.001",
            CustomerAccounts = ["SHOPRITE"],
            InvoiceDateFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)
        };

    private static HttpResponseMessage JsonResponse(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };

    private sealed class SequenceHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new(responses);

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
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, body, cookie));
            return _responses.Dequeue()(request);
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? Body, string? Cookie);
}
