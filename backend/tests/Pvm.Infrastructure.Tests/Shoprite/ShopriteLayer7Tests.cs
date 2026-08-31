using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pvm.Infrastructure.Shoprite;

namespace Pvm.Infrastructure.Tests.Shoprite;

public sealed class ShopriteLayer7Tests
{
    [Fact]
    public async Task Every_request_carries_the_layer_seven_headers()
    {
        using var inner = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"orderField":[]}""")
        });
        using var layer7 = new ShopriteLayer7Handler(Options.Create(DefaultOptions()))
        {
            InnerHandler = inner
        };
        using var httpClient = new HttpClient(layer7);
        var client = new ShopritePurchaseOrderClient(httpClient, Options.Create(DefaultOptions()));

        await client.FetchAsync(CancellationToken.None);

        var request = Assert.IsType<HttpRequestMessage>(inner.Request);
        Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
        Assert.Equal(
            Convert.ToBase64String("api-user:secret"u8.ToArray()),
            request.Headers.Authorization?.Parameter);
        Assert.Equal("contract-123", Assert.Single(request.Headers.GetValues("ContractID")));
        Assert.Equal("api-user", Assert.Single(request.Headers.GetValues("UIUser")));
    }

    [Fact]
    public async Task A_separate_ui_user_overrides_the_username()
    {
        using var inner = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"orderField":[]}""")
        });
        var options = DefaultOptions() with { UiUser = "PVMProd" };
        using var layer7 = new ShopriteLayer7Handler(Options.Create(options)) { InnerHandler = inner };
        using var httpClient = new HttpClient(layer7);
        var client = new ShopritePurchaseOrderClient(httpClient, Options.Create(options));

        await client.FetchAsync(CancellationToken.None);

        Assert.Equal("PVMProd", Assert.Single(inner.Request!.Headers.GetValues("UIUser")));
    }

    [Fact]
    public async Task The_headers_stay_off_for_the_supplier_services_host()
    {
        using var inner = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"orderField":[]}""")
        });
        var options = DefaultOptions() with { UseLayer7Headers = false };
        using var layer7 = new ShopriteLayer7Handler(Options.Create(options)) { InnerHandler = inner };
        using var httpClient = new HttpClient(layer7);
        var client = new ShopritePurchaseOrderClient(httpClient, Options.Create(options));

        await client.FetchAsync(CancellationToken.None);

        var request = Assert.IsType<HttpRequestMessage>(inner.Request);
        Assert.Null(request.Headers.Authorization);
        Assert.False(request.Headers.Contains("ContractID"));
        Assert.False(request.Headers.Contains("UIUser"));
    }

    [Fact]
    public async Task A_missing_contract_identifier_stops_the_call()
    {
        using var inner = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var options = DefaultOptions() with { ContractId = null };
        using var layer7 = new ShopriteLayer7Handler(Options.Create(options)) { InnerHandler = inner };
        using var httpClient = new HttpClient(layer7);
        var client = new ShopritePurchaseOrderClient(httpClient, Options.Create(options));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.FetchAsync(CancellationToken.None));

        Assert.Contains("ContractId", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(inner.Request);
    }

    [Fact]
    public async Task Acknowledgement_puts_the_order_numbers_to_the_action_endpoint()
    {
        using var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        var client = new ShopritePurchaseOrderClient(httpClient, Options.Create(DefaultOptions()));

        await client.AcknowledgeAsync(["1212021109", "1215382915"], CancellationToken.None);

        var request = Assert.IsType<HttpRequestMessage>(handler.Request);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal(
            "https://shoprite.example/B2BWebAPISupplierServices/api/VendorOrder",
            request.RequestUri?.GetLeftPart(UriPartial.Path));
        Assert.Contains("action=A", request.RequestUri?.Query);
        var numbers = JsonSerializer.Deserialize<long[]>(handler.Body!);
        Assert.NotNull(numbers);
        Assert.Equal([1212021109L, 1215382915L], numbers!);
    }

    [Fact]
    public async Task Reset_uses_the_reset_action()
    {
        using var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        var client = new ShopritePurchaseOrderClient(httpClient, Options.Create(DefaultOptions()));

        await client.ResetAsync(["1212021109"], CancellationToken.None);

        Assert.Contains("action=Reset", handler.Request!.RequestUri?.Query);
    }

    [Fact]
    public async Task An_empty_acknowledgement_makes_no_call()
    {
        using var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        var client = new ShopritePurchaseOrderClient(httpClient, Options.Create(DefaultOptions()));

        await client.AcknowledgeAsync([], CancellationToken.None);

        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task A_failed_acknowledgement_reports_the_status_code()
    {
        using var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("upstream unavailable")
        });
        using var httpClient = new HttpClient(handler);
        var client = new ShopritePurchaseOrderClient(httpClient, Options.Create(DefaultOptions()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.AcknowledgeAsync(["1212021109"], CancellationToken.None));

        Assert.Contains("HTTP 502", exception.Message);
    }

    [Fact]
    public async Task A_non_numeric_order_number_is_refused_before_any_call()
    {
        using var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        var client = new ShopritePurchaseOrderClient(httpClient, Options.Create(DefaultOptions()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.AcknowledgeAsync(["QA-SEED-1"], CancellationToken.None));

        Assert.Null(handler.Request);
    }

    private static ShopriteOptions DefaultOptions()
        => new()
        {
            BaseUrl = "https://shoprite.example/B2BWebAPISupplierServices/api",
            Username = "api-user",
            Password = "secret",
            ContractId = "contract-123",
            UseLayer7Headers = true
        };

    private sealed class CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            if (request.Content is not null)
            {
                Body = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return send(request);
        }
    }
}
