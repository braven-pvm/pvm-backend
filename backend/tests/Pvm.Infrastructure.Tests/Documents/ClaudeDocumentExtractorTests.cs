using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        var handler = new FakeHandler(() => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(
                """{"type":"error","error":{"type":"api_error","message":"boom"}}""",
                Encoding.UTF8,
                "application/json"),
        });

        await Assert.ThrowsAnyAsync<Exception>(() => Extract(handler));
    }

    [Fact]
    public async Task Extract_MissingApiKey_ThrowsInvalidOperation()
    {
        var handler = new FakeHandler(MessageResponse("end_turn", ValidFields));
        var extractor = new ClaudeDocumentExtractor(
            Options.Create(new AnthropicOptions { ApiKey = " " }),
            new HttpClient(handler));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            extractor.ExtractAsync(new ProofOfDeliveryDocumentType(), new MemoryStream(PdfBytes), CancellationToken.None));

        Assert.Contains("Anthropic:ApiKey", error.Message);
        Assert.Empty(handler.Requests);
    }

    private static Task<ExtractionResult> Extract(
        FakeHandler handler,
        IDocumentType? type = null)
    {
        var extractor = new ClaudeDocumentExtractor(
            Options.Create(new AnthropicOptions { ApiKey = "test-key" }),
            new HttpClient(handler));
        return extractor.ExtractAsync(
            type ?? new ProofOfDeliveryDocumentType(),
            new MemoryStream(PdfBytes),
            CancellationToken.None);
    }

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
}
