using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Pvm.Application.Stock;
using Pvm.Infrastructure.Stock;
using Xunit;

namespace Pvm.Infrastructure.Tests.Stock;

public sealed class WooCommerceStockClientTests
{
    [Fact]
    public async Task ListStockItemsAsync_reads_products_and_variations_and_how_each_holds_stock()
    {
        using var handler = new RouteHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/wp-json/wc/v3/products" when Page(request) == 1 => Json("""
                [{"id":10,"sku":"BOTT8","type":"simple","manage_stock":false,"stock_quantity":null},
                 {"id":20,"sku":"","type":"variable","manage_stock":true,"stock_quantity":458},
                 {"id":30,"sku":"KLER5","type":"simple","manage_stock":true,"stock_quantity":5}]
                """),
            "/wp-json/wc/v3/products/20/variations" when Page(request) == 1 => Json("""
                [{"id":201,"sku":"ENER1","manage_stock":"parent","stock_quantity":458},
                 {"id":202,"sku":"DYNA24","manage_stock":true,"stock_quantity":12}]
                """),
            _ => Json("[]"),
        });
        var client = NewClient(handler, pageSize: 100);

        var items = await client.ListStockItemsAsync(CancellationToken.None);

        Assert.Equal(4, items.Count);
        Assert.Equal(WebStockManagement.None, items.Single(i => i.Sku == "BOTT8").Management);
        Assert.Equal(WebStockManagement.Own, items.Single(i => i.Sku == "KLER5").Management);
        var ener1 = items.Single(i => i.Sku == "ENER1");
        Assert.Equal(WebStockManagement.Parent, ener1.Management);
        Assert.Equal(20, ener1.ProductId);
        Assert.Equal(201, ener1.VariationId);
        Assert.Equal(12, items.Single(i => i.Sku == "DYNA24").CurrentQuantity);
    }

    [Fact]
    public async Task A_bot_protection_page_is_a_failure_not_an_empty_store()
    {
        // The site once answered every API call with HTTP 200 and an HTML "One moment, please..." page.
        using var handler = new RouteHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html><title>One moment, please...</title></html>", Encoding.UTF8, "text/html"),
        });
        var client = NewClient(handler);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ListStockItemsAsync(CancellationToken.None));

        Assert.Contains("not JSON", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListPendingOrderLinesAsync_asks_for_open_statuses_and_reads_each_line()
    {
        string? query = null;
        using var handler = new RouteHandler(request =>
        {
            query = request.RequestUri!.Query;
            return Page(request) == 1
                ? Json("""
                    [{"id":5001,"line_items":[{"sku":"ENER1","quantity":2},{"sku":"","quantity":1}]},
                     {"id":5002,"line_items":[{"sku":"SION10","quantity":3}]}]
                    """)
                : Json("[]");
        });
        var client = NewClient(handler);

        var lines = await client.ListPendingOrderLinesAsync(CancellationToken.None);

        Assert.Contains("status=on-hold,processing", Uri.UnescapeDataString(query!), StringComparison.Ordinal);
        Assert.Equal(2, lines.Count);
        Assert.Equal(new WebOrderLine("5001", "ENER1", 2), lines[0]);
        Assert.Equal(new WebOrderLine("5002", "SION10", 3), lines[1]);
    }

    [Fact]
    public async Task SetStockQuantityAsync_writes_to_the_variation()
    {
        HttpRequestMessage? sent = null;
        string? body = null;
        using var handler = new RouteHandler(request =>
        {
            sent = request;
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json("""{"id":202,"stock_quantity":12}""");
        });
        var client = NewClient(handler);

        await client.SetStockQuantityAsync(
            new WebStockItem("DYNA24", 20, 202, WebStockManagement.Own, 15),
            12,
            CancellationToken.None);

        Assert.Equal(HttpMethod.Put, sent!.Method);
        Assert.Equal("/wp-json/wc/v3/products/20/variations/202", sent.RequestUri!.AbsolutePath);
        Assert.Equal("""{"stock_quantity":12}""", body);
        Assert.Equal("Basic", sent.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task SetStockQuantityAsync_refuses_an_item_whose_stock_belongs_to_the_parent()
    {
        using var handler = new RouteHandler(_ => Json("{}"));
        var client = NewClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SetStockQuantityAsync(
            new WebStockItem("ENER1", 20, 201, WebStockManagement.Parent, 458),
            7000,
            CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    private static WooCommerceStockClient NewClient(HttpMessageHandler handler, int pageSize = 2)
        => new(new HttpClient(handler), Options.Create(new WooCommerceOptions
        {
            BaseUrl = "https://store.example",
            ConsumerKey = "ck_test",
            ConsumerSecret = "cs_test",
            PageSize = pageSize,
        }));

    private static int Page(HttpRequestMessage request)
    {
        var query = request.RequestUri!.Query.TrimStart('?').Split('&');
        var page = query.FirstOrDefault(part => part.StartsWith("page=", StringComparison.Ordinal));
        return page is null ? 1 : int.Parse(page["page=".Length..], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class RouteHandler(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(route(request));
        }
    }
}
