using System.Net;
using Microsoft.Extensions.Options;
using Pvm.Infrastructure.Shoprite;

namespace Pvm.Infrastructure.Tests.Shoprite;

public sealed class ShopritePurchaseOrderClientTests
{
    [Fact]
    public async Task FetchAsync_UsesVendorOrderEndpointWithQueryCredentials()
    {
        using var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"orderField":[]}""")
        });
        using var httpClient = new HttpClient(handler);
        var client = new ShopritePurchaseOrderClient(httpClient, Options.Create(DefaultOptions()));

        await client.FetchAsync(CancellationToken.None);

        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Get, handler.Request.Method);
        Assert.Equal("https://shoprite.example/B2BWebAPISupplierServices/api/VendorOrder", handler.Request.RequestUri?.GetLeftPart(UriPartial.Path));
        Assert.Contains("userName=api-user", handler.Request.RequestUri?.Query);
        Assert.Contains("password=secret", handler.Request.RequestUri?.Query);
    }

    [Fact]
    public async Task FetchAsync_NonSuccessThrowsWithoutLeakingResponseBody()
    {
        using var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("credential failure body")
        });
        using var httpClient = new HttpClient(handler);
        var client = new ShopritePurchaseOrderClient(httpClient, Options.Create(DefaultOptions()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.FetchAsync(CancellationToken.None));

        Assert.Contains("HTTP 403", exception.Message);
        Assert.DoesNotContain("credential failure body", exception.Message);
    }

    private static ShopriteOptions DefaultOptions()
        => new()
        {
            BaseUrl = "https://shoprite.example/B2BWebAPISupplierServices/api",
            Username = "api-user",
            Password = "secret"
        };

    private sealed class CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(send(request));
        }
    }
}

