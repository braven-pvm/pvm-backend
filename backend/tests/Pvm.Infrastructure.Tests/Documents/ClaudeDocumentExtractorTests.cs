using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pvm.Application.Documents;
using Pvm.Infrastructure.Documents;

namespace Pvm.Infrastructure.Tests.Documents;

public sealed class ClaudeDocumentExtractorTests
{
    private const string ValidFields = """
        {
          "InvoiceNumbers": ["INV001234"],
          "SalesOrderNumbers": [],
          "CustomerOrderNumbers": ["PO-77"],
          "ClientName": "Acme Stores",
          "DeliveryDate": "2026-11-02",
          "SignaturePresent": true,
          "Note": null
        }
        """;

    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7 fake scan");

    [Fact]
    public async Task Extract_ValidJson_ReturnsExtracted()
    {
        var handler = new FakeHandler(MessageResponse("end_turn", ValidFields));

        var result = await Extract(handler);

        var extracted = Assert.IsType<Extracted>(result);
        Assert.Equal("INV001234", extracted.Fields.GetProperty("InvoiceNumbers")[0].GetString());
        Assert.True(extracted.Fields.GetProperty("SignaturePresent").GetBoolean());
    }

    [Fact]
    public async Task Extract_Refusal_ReturnsUnreadable()
    {
        var handler = new FakeHandler(MessageResponse("refusal", ValidFields));

        var result = await Extract(handler);

        Assert.Equal(new Unreadable("refusal"), result);
    }

    [Fact]
    public async Task Extract_MaxTokens_ReturnsUnreadable()
    {
        var handler = new FakeHandler(MessageResponse("max_tokens", "{\"InvoiceNumbers\": [\"INV"));

        var result = await Extract(handler);

        Assert.Equal(new Unreadable("max_tokens"), result);
    }

    [Theory]
    [InlineData("""{"InvoiceNumbers": []}""")]
    [InlineData("""["not", "an", "object"]""")]
    [InlineData("""
        {
          "InvoiceNumbers": [], "SalesOrderNumbers": [], "CustomerOrderNumbers": [],
          "ClientName": null, "DeliveryDate": null, "SignaturePresent": false, "Note": null,
          "Extra": 1
        }
        """)]
    public async Task Extract_JsonFailsSchema_ReturnsUnreadable(string text)
    {
        var handler = new FakeHandler(MessageResponse("end_turn", text));

        var result = await Extract(handler);

        Assert.Equal(new Unreadable("schema"), result);
    }

    [Fact]
    public async Task Extract_TextIsNotJson_ReturnsUnreadable()
    {
        var handler = new FakeHandler(MessageResponse("end_turn", "I could not read this page."));

        var result = await Extract(handler);

        Assert.Equal(new Unreadable("malformed"), result);
    }

    [Fact]
    public async Task Extract_NoTextBlock_ReturnsUnreadable()
    {
        var handler = new FakeHandler(MessageResponse("end_turn", text: null));

        var result = await Extract(handler);

        Assert.Equal(new Unreadable("empty"), result);
    }

    [Fact]
    public async Task Extract_RequestSendsPdfAsDocumentBlock_AndSchemaAsOutputFormat()
    {
        var handler = new FakeHandler(MessageResponse("end_turn", ValidFields));
        var type = new ProofOfDeliveryDocumentType();

        await Extract(handler, type);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v1/messages", request.Path);
        Assert.Contains("server-side-fallback-2026-07-01", request.Betas);
        Assert.Equal("Bearer test-key", request.Authorization);

        var body = request.Body;
        Assert.Equal("claude-opus-5-5", body["model"]!.GetValue<string>());
        Assert.Equal(4000, body["max_tokens"]!.GetValue<int>());
        Assert.Equal("default", body["fallbacks"]!.GetValue<string>());
        Assert.Null(body["thinking"]);

        var message = Assert.Single(body["messages"]!.AsArray())!;
        Assert.Equal("user", message["role"]!.GetValue<string>());
        var content = message["content"]!.AsArray();
        Assert.Equal(2, content.Count);
        Assert.Equal("document", content[0]!["type"]!.GetValue<string>());
        Assert.Equal("base64", content[0]!["source"]!["type"]!.GetValue<string>());
        Assert.Equal("application/pdf", content[0]!["source"]!["media_type"]!.GetValue<string>());
        Assert.Equal(Convert.ToBase64String(PdfBytes), content[0]!["source"]!["data"]!.GetValue<string>());
        Assert.Equal("text", content[1]!["type"]!.GetValue<string>());
        Assert.Equal(type.ExtractionInstruction, content[1]!["text"]!.GetValue<string>());

        var outputConfig = body["output_config"]!;
        Assert.Equal("low", outputConfig["effort"]!.GetValue<string>());
        Assert.Equal("json_schema", outputConfig["format"]!["type"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse(type.FieldSchema.GetRawText()),
            outputConfig["format"]!["schema"]));
    }

    [Fact]
    public async Task Extract_ServerError_Throws()
    {
        var handler = new FakeHandler(ErrorResponse(HttpStatusCode.InternalServerError, "api_error"));

        await Assert.ThrowsAsync<Anthropic5xxException>(() => Extract(handler));
    }

    [Fact]
    public async Task Extract_RateLimited_Throws()
    {
        var handler = new FakeHandler(ErrorResponse(HttpStatusCode.TooManyRequests, "rate_limit_error"));

        await Assert.ThrowsAsync<AnthropicRateLimitException>(() => Extract(handler));
    }

    [Fact]
    public async Task Extract_BadRequest_ReturnsUnreadable()
    {
        var handler = new FakeHandler(ErrorResponse(HttpStatusCode.BadRequest, "invalid_request_error"));

        var result = await Extract(handler);

        Assert.Equal(new Unreadable("rejected"), result);
    }

    [Fact]
    public async Task Extract_RequestTooLarge_ReturnsUnreadable()
    {
        var handler = new FakeHandler(ErrorResponse(HttpStatusCode.RequestEntityTooLarge, "request_too_large"));

        var result = await Extract(handler);

        Assert.Equal(new Unreadable("rejected"), result);
    }

    [Fact]
    public async Task Extract_PdfTooLargeForApi_ReturnsUnreadableWithoutCall()
    {
        var handler = new FakeHandler(MessageResponse("end_turn", ValidFields));
        // 23,250,001 bytes -> 31,000,004 base64 characters, just over the 31,000,000 limit.
        var pdf = new byte[23_250_001];

        var result = await Extract(handler, pdf: pdf);

        Assert.Equal(new Unreadable("too_large"), result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Extract_AllFieldsEmpty_ReturnsUnreadable()
    {
        var handler = new FakeHandler(MessageResponse("end_turn", """
            {
              "InvoiceNumbers": [], "SalesOrderNumbers": [], "CustomerOrderNumbers": [],
              "ClientName": null, "DeliveryDate": null, "SignaturePresent": true, "Note": null
            }
            """));

        var result = await Extract(handler);

        Assert.Equal(new Unreadable("empty"), result);
    }

    [Fact]
    public async Task Extract_MissingApiKey_ThrowsInvalidOperation()
    {
        var handler = new FakeHandler(MessageResponse("end_turn", ValidFields));
        var extractor = new ClaudeDocumentExtractor(
            Options.Create(new AnthropicOptions { ApiKey = " " }),
            logger: null,
            new HttpClient(handler),
            maxRetries: 0);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            extractor.ExtractAsync(new ProofOfDeliveryDocumentType(), new MemoryStream(PdfBytes), CancellationToken.None));

        Assert.Contains("Anthropic:ApiKey", error.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Extract_MissingApiKey_LogsAnError()
    {
        var handler = new FakeHandler(MessageResponse("end_turn", ValidFields));
        var logger = new ListLogger();
        var extractor = new ClaudeDocumentExtractor(
            Options.Create(new AnthropicOptions { ApiKey = "" }),
            logger,
            new HttpClient(handler),
            maxRetries: 0);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            extractor.ExtractAsync(new ProofOfDeliveryDocumentType(), new MemoryStream(PdfBytes), CancellationToken.None));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("Anthropic:ApiKey", entry.Message);
    }

    private static Task<ExtractionResult> Extract(
        FakeHandler handler,
        IDocumentType? type = null,
        byte[]? pdf = null)
    {
        var extractor = new ClaudeDocumentExtractor(
            Options.Create(new AnthropicOptions { ApiKey = "test-key" }),
            logger: null,
            new HttpClient(handler),
            maxRetries: 0);
        return extractor.ExtractAsync(
            type ?? new ProofOfDeliveryDocumentType(),
            new MemoryStream(pdf ?? PdfBytes),
            CancellationToken.None);
    }

    private static Func<HttpResponseMessage> ErrorResponse(HttpStatusCode status, string errorType)
        => () => new HttpResponseMessage(status)
        {
            Content = new StringContent(
                $$$"""{"type":"error","error":{"type":"{{{errorType}}}","message":"test"}}""",
                Encoding.UTF8,
                "application/json"),
        };

    private static Func<HttpResponseMessage> MessageResponse(string stopReason, string? text)
    {
        var content = new JsonArray();
        content.Add(new JsonObject { ["type"] = "thinking", ["thinking"] = "", ["signature"] = "sig" });
        if (text is not null)
        {
            content.Add(new JsonObject { ["type"] = "text", ["text"] = text });
        }

        var body = new JsonObject
        {
            ["id"] = "msg_test",
            ["type"] = "message",
            ["role"] = "assistant",
            ["model"] = "claude-opus-5-5",
            ["content"] = content,
            ["stop_reason"] = stopReason,
            ["stop_sequence"] = null,
            ["stop_details"] = stopReason == "refusal"
                ? new JsonObject { ["type"] = "refusal", ["category"] = null, ["explanation"] = null }
                : null,
            ["container"] = null,
            ["context_management"] = null,
            ["usage"] = new JsonObject { ["input_tokens"] = 10, ["output_tokens"] = 20 },
        }.ToJsonString();

        return () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private sealed record CapturedRequest(
        string Path,
        string Betas,
        string? Authorization,
        JsonNode Body);

    private sealed class FakeHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? "{}"
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.RequestUri!.AbsolutePath,
                request.Headers.TryGetValues("anthropic-beta", out var betas) ? string.Join(",", betas) : "",
                request.Headers.Authorization?.ToString(),
                JsonNode.Parse(body)!));
            return respond();
        }
    }

    private sealed class ListLogger : ILogger<ClaudeDocumentExtractor>
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
