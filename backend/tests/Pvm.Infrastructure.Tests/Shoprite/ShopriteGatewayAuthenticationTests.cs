using System.Net;
using Microsoft.Extensions.Options;
using Pvm.Infrastructure.Operations;
using Pvm.Infrastructure.Shoprite;

namespace Pvm.Infrastructure.Tests.Shoprite;

public sealed class ShopriteGatewayAuthenticationTests
{
    [Fact]
    public async Task The_gateway_request_carries_no_credentials_in_the_query_string()
    {
        using var handler = new CaptureHandler(HttpStatusCode.OK, """{"orderField":[]}""");
        using var httpClient = new HttpClient(handler);
        var client = new ShopritePurchaseOrderClient(httpClient, Options.Create(Gateway()));

        await client.FetchAsync(CancellationToken.None);

        var uri = handler.Request!.RequestUri!;
        Assert.Equal("https://externalservices.example/b2bservice/api/VendorOrder", uri.ToString());
        Assert.DoesNotContain("password", uri.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_gateway_acknowledgement_keeps_only_the_action()
    {
        using var handler = new CaptureHandler(HttpStatusCode.OK, "");
        using var httpClient = new HttpClient(handler);
        var client = new ShopritePurchaseOrderClient(httpClient, Options.Create(Gateway()));

        await client.AcknowledgeAsync(["1212021109"], CancellationToken.None);

        Assert.Equal("?action=A", handler.Request!.RequestUri!.Query);
    }

    [Fact]
    public async Task The_supplier_services_host_keeps_query_credentials()
    {
        using var handler = new CaptureHandler(HttpStatusCode.OK, """{"orderField":[]}""");
        using var httpClient = new HttpClient(handler);
        var client = new ShopritePurchaseOrderClient(httpClient, Options.Create(Gateway() with { UseLayer7Headers = false }));

        await client.FetchAsync(CancellationToken.None);

        Assert.Contains("userName=api-user", handler.Request!.RequestUri!.Query);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Rejected_credentials_raise_a_distinct_exception(HttpStatusCode status)
    {
        using var handler = new CaptureHandler(status, """{"fault":{"faultcode":"SH-401-EXT"}}""");
        using var httpClient = new HttpClient(handler);
        var client = new ShopritePurchaseOrderClient(httpClient, Options.Create(Gateway()));

        var exception = await Assert.ThrowsAsync<IntegrationCredentialsRejectedException>(
            () => client.FetchAsync(CancellationToken.None));

        Assert.Equal("Shoprite", exception.System);
        Assert.Equal((int)status, exception.StatusCode);
    }

    private static ShopriteOptions Gateway()
        => new()
        {
            BaseUrl = "https://externalservices.example/b2bservice/api",
            Username = "api-user",
            Password = "p@ss",
            ContractId = "contract-123",
            UseLayer7Headers = true
        };

    private sealed class CaptureHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
