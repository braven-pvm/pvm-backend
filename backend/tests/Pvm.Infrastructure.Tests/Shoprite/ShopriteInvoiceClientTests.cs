using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pvm.Application.Submissions;
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
    public async Task SubmitAsync_CallerCancellationPropagatesOperationCanceledException()
    {
        using var handler = new CaptureHandler(_ => throw new OperationCanceledException());
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://shoprite.example/")
        };
        var client = new ShopriteInvoiceClient(httpClient, Options.Create(DefaultOptions()));
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        var exception = await Record.ExceptionAsync(
            () => client.SubmitAsync("<invoice />", cancellationTokenSource.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
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

    [Theory]
    [InlineData("BaseUrl", "")]
    [InlineData("BaseUrl", "   ")]
    [InlineData("Username", "")]
    [InlineData("Username", "   ")]
    [InlineData("Password", "")]
    [InlineData("Password", "   ")]
    [InlineData("ContractId", "")]
    [InlineData("ContractId", "   ")]
    [InlineData("UiUser", "")]
    [InlineData("UiUser", "   ")]
    public void AddShopriteClient_InvalidRequiredOptionFailsClearly(string key, string value)
    {
        var configurationValues = DefaultConfiguration();
        configurationValues[$"Shoprite:{key}"] = value;

        var exception = Assert.Throws<OptionsValidationException>(
            () => CreateConfiguredClient(configurationValues));

        Assert.Contains(key, exception.Message);
    }

    [Theory]
    [InlineData("http://shoprite.example/")]
    [InlineData("/VendorInvoice")]
    [InlineData("shoprite.example")]
    public void AddShopriteClient_InvalidBaseUrlFailsClearly(string baseUrl)
    {
        var configurationValues = DefaultConfiguration();
        configurationValues["Shoprite:BaseUrl"] = baseUrl;

        var exception = Assert.Throws<OptionsValidationException>(
            () => CreateConfiguredClient(configurationValues));

        Assert.Contains("BaseUrl", exception.Message);
        Assert.Contains("absolute HTTPS URI", exception.Message);
    }

    [Fact]
    public void AddShopriteClient_ValidOptionsResolveClient()
    {
        var client = CreateConfiguredClient(DefaultConfiguration());

        Assert.IsAssignableFrom<IShopriteInvoiceClient>(client);
    }

    private static IShopriteInvoiceClient CreateConfiguredClient(
        Dictionary<string, string?> configurationValues)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();
        var services = new ServiceCollection();
        services.AddShopriteClient(configuration);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IShopriteInvoiceClient>();
    }

    private static Dictionary<string, string?> DefaultConfiguration()
    {
        return new Dictionary<string, string?>
        {
            ["Shoprite:BaseUrl"] = "https://shoprite.example/",
            ["Shoprite:Username"] = "api-user",
            ["Shoprite:Password"] = "secret",
            ["Shoprite:ContractId"] = "contract-123",
            ["Shoprite:UiUser"] = "ui-user"
        };
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
