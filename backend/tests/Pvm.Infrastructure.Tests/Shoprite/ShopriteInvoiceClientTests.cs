using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using Pvm.Infrastructure.Shoprite;

namespace Pvm.Infrastructure.Tests.Shoprite;

public sealed class ShopriteInvoiceClientTests
{
    [Fact]
    public async Task SubmitAsync_SetsBasicAuthenticationHeader()
    {
        using var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://shoprite.example/")
        };
        var client = new ShopriteInvoiceClient(httpClient, Options.Create(DefaultOptions()));

        await client.SubmitAsync("<invoice />", CancellationToken.None);

        Assert.NotNull(handler.Request?.Headers.Authorization);
        var authorization = handler.Request.Headers.Authorization;
        Assert.Equal("Basic", authorization.Scheme);
        Assert.Equal(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("api-user:secret")), authorization.Parameter);
    }

    [Fact]
    public async Task SubmitAsync_SetsContractIdAndUiUserHeaders()
    {
        using var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://shoprite.example/")
        };
        var client = new ShopriteInvoiceClient(httpClient, Options.Create(DefaultOptions()));

        await client.SubmitAsync("<invoice />", CancellationToken.None);

        Assert.Equal("contract-123", Assert.Single(handler.Request?.Headers.GetValues("ContractID") ?? []));
        Assert.Equal("ui-user", Assert.Single(handler.Request?.Headers.GetValues("UIUser") ?? []));
    }

    [Fact]
    public async Task SubmitAsync_UsesXmlContentAndAcceptHeaders()
    {
        using var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://shoprite.example/")
        };
        var client = new ShopriteInvoiceClient(httpClient, Options.Create(DefaultOptions()));

        await client.SubmitAsync("<invoice />", CancellationToken.None);

        Assert.Equal("application/xml", handler.Request?.Content?.Headers.ContentType?.MediaType);
        Assert.NotNull(handler.Request);
        Assert.Contains(handler.Request.Headers.Accept, header => header.MediaType == "application/xml");
    }

    [Fact]
    public async Task SubmitAsync_TransportUncertaintyReturnsAmbiguousResponse()
    {
        using var handler = new CaptureHandler(_ => throw new HttpRequestException("connection reset"));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://shoprite.example/")
        };
        var client = new ShopriteInvoiceClient(httpClient, Options.Create(DefaultOptions()));

        var response = await client.SubmitAsync("<invoice />", CancellationToken.None);

        Assert.False(response.Success);
        Assert.Null(response.StatusCode);
        Assert.True(response.IsAmbiguous);
        Assert.Equal("connection reset", response.Body);
    }

    [Fact]
    public async Task SubmitAsync_TimeoutReturnsAmbiguousResponse()
    {
        using var handler = new CaptureHandler(_ => throw new OperationCanceledException("timeout"));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://shoprite.example/")
        };
        var client = new ShopriteInvoiceClient(httpClient, Options.Create(DefaultOptions()));

        var response = await client.SubmitAsync("<invoice />", CancellationToken.None);

        Assert.False(response.Success);
        Assert.Null(response.StatusCode);
        Assert.True(response.IsAmbiguous);
        Assert.Equal("Shoprite request timed out.", response.Body);
    }

    [Fact]
    public async Task SubmitAsync_NonSuccessResponseCapturesStatusCodeAndBody()
    {
        using var handler = new CaptureHandler(
            _ => new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("upstream failure")
            });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://shoprite.example/")
        };
        var client = new ShopriteInvoiceClient(httpClient, Options.Create(DefaultOptions()));

        var response = await client.SubmitAsync("<invoice />", CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(502, response.StatusCode);
        Assert.Equal("upstream failure", response.Body);
        Assert.False(response.IsAmbiguous);
    }

    private static ShopriteOptions DefaultOptions()
    {
        return new ShopriteOptions
        {
            BaseUrl = "https://shoprite.example/",
            Username = "api-user",
            Password = "secret",
            ContractId = "contract-123",
            UiUser = "ui-user"
        };
    }

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
