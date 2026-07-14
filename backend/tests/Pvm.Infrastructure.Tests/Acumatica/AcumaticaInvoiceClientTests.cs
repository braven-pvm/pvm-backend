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
        const string salesInvoiceJson = """
            [
              {
                "id": "c340d968-71a3-4ced-8c91-7cf1e653bec4",
                "ReferenceNbr": { "value": "INV000123" },
                "Status": { "value": "Open" },
                "CustomerID": { "value": "SHOPRITE" },
                "LocationID": { "value": "CPT-DC" },
                "CustomerOrder": { "value": "1210297232" },
                "Currency": { "value": "ZAR" },
                "Date": { "value": "2026-07-14T00:00:00+02:00" },
                "Amount": { "value": 115.00 },
                "TaxTotal": { "value": 15.00 },
                "Details": [
                  {
                    "LineNbr": { "value": 1 },
                    "InventoryID": { "value": "PVM-ITEM-1" },
                    "Description": { "value": "PVM test item" },
                    "Qty": { "value": 2.00 },
                    "UOM": { "value": "EA" },
                    "UnitPrice": { "value": 50.00 },
                    "ExtendedPrice": { "value": 100.00 },
                    "TaxAmount": { "value": 15.00 },
                    "TaxCategory": { "value": "STANDARD" }
                  }
                ]
              }
            ]
            """;
        using var handler = new SequenceHandler(
            _ => new HttpResponseMessage(HttpStatusCode.NoContent),
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
        Assert.Equal("CPT-DC", invoice.CustomerLocation);
        Assert.Equal("1210297232", invoice.CustomerOrder);
        Assert.Equal("ZAR", invoice.CurrencyCode);
        Assert.Equal("ZA", invoice.CountryCode);
        Assert.Equal(100.00m, invoice.TotalExcludingTax);
        Assert.Equal(115.00m, invoice.TotalIncludingTax);
        Assert.Equal(15.00m, invoice.TotalTax);

        var line = Assert.Single(invoice.Lines);
        Assert.Equal(1, line.LineNumber);
        Assert.Equal("PVM-ITEM-1", line.InventoryId);
        Assert.Equal("PVM test item", line.Description);
        Assert.Equal(2.00m, line.Quantity);
        Assert.Equal("EA", line.Uom);
        Assert.Equal(50.00m, line.UnitAmountExcludingTax);
        Assert.Equal(57.50m, line.UnitAmountIncludingTax);
        Assert.Equal(7.50m, line.TaxAmount);
        Assert.Equal("STANDARD", line.TaxCategoryCode);
        Assert.Equal(15.00m, line.TaxPercentage);
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
        Assert.Contains("$expand=Details", query);
        Assert.Contains("Status eq 'Open'", query);
        Assert.Contains("Status eq 'Closed'", query);
        Assert.Contains("CustomerID eq 'SHOPRITE'", query);
        Assert.Contains("CustomerID eq 'CHECKERS'", query);
        Assert.Contains("$top=75", query);
        Assert.Contains("$skip=0", query);
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
            CustomerAccounts = ["SHOPRITE"]
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
