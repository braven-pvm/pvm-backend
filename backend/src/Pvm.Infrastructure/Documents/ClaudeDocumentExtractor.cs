using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Options;
using Pvm.Application.Documents;

namespace Pvm.Infrastructure.Documents;

/// <summary>
/// Sends a scanned PDF to Claude with the document type's field schema as structured output
/// (design 6.4). Content problems return <see cref="Unreadable"/>; transport failures
/// (network, 429, 5xx after the SDK's retries) throw so the queue message is retried.
/// </summary>
public sealed class ClaudeDocumentExtractor : IDocumentExtractor
{
    private const int MaxTokens = 4000;
    private const string FallbackBeta = "server-side-fallback-2026-07-01";

    private readonly AnthropicOptions _options;
    private readonly HttpClient? _httpClient;
    private readonly Lazy<AnthropicClient> _client;

    public ClaudeDocumentExtractor(IOptions<AnthropicOptions> options)
        : this(options, httpClient: null)
    {
    }

    /// <param name="httpClient">The HTTP client the SDK uses; null for the SDK default.</param>
    public ClaudeDocumentExtractor(IOptions<AnthropicOptions> options, HttpClient? httpClient)
    {
        _options = options.Value;
        _httpClient = httpClient;
        _client = new Lazy<AnthropicClient>(CreateClient);
    }

    public async Task<ExtractionResult> ExtractAsync(
        IDocumentType type,
        Stream pdf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(pdf);
        var client = _client.Value;

        using var buffer = new MemoryStream();
        await pdf.CopyToAsync(buffer, cancellationToken);
        var base64 = Convert.ToBase64String(buffer.GetBuffer(), 0, (int)buffer.Length);

        var parameters = new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = MaxTokens,
            Betas = [FallbackBeta],
            Fallbacks = new Default(),
            OutputConfig = new BetaOutputConfig
            {
                Effort = Effort.Low,
                Format = new BetaJsonOutputFormat { Schema = ToSchemaDictionary(type.FieldSchema) },
            },
            Messages =
            [
                new BetaUserMessageParam
                {
                    Content = new List<BetaContentBlockParam>
                    {
                        new BetaRequestDocumentBlock { Source = new BetaBase64PdfSource { Data = base64 } },
                        new BetaTextBlockParam { Text = type.ExtractionInstruction },
                    },
                },
            ],
        };

        var response = await client.Beta.Messages.Create(parameters, cancellationToken);

        var stopReason = response.StopReason?.Raw();
        if (stopReason is "refusal" or "max_tokens")
        {
            return new Unreadable(stopReason);
        }

        string? text = null;
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var textBlock))
            {
                text = textBlock.Text;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return new Unreadable("empty");
        }

        JsonElement fields;
        try
        {
            using var document = JsonDocument.Parse(text);
            fields = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new Unreadable("malformed");
        }

        return MatchesTopLevelShape(fields, type.FieldSchema)
            ? new Extracted(fields)
            : new Unreadable("schema");
    }

    private AnthropicClient CreateClient()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "Anthropic:ApiKey is not configured (Key Vault secret Anthropic--ApiKey); "
                + "document extraction cannot run.");
        }

        return _httpClient is null
            ? new AnthropicClient { ApiKey = _options.ApiKey }
            : new AnthropicClient { ApiKey = _options.ApiKey, HttpClient = _httpClient };
    }

    private static Dictionary<string, JsonElement> ToSchemaDictionary(JsonElement schema)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in schema.EnumerateObject())
        {
            result[property.Name] = property.Value.Clone();
        }

        return result;
    }

    /// <summary>
    /// Checks the top level only: an object, every <c>required</c> property present, and no
    /// other property when <c>additionalProperties</c> is false. Not a full JSON Schema validator.
    /// </summary>
    private static bool MatchesTopLevelShape(JsonElement value, JsonElement schema)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var name in required.EnumerateArray())
            {
                if (name.ValueKind != JsonValueKind.String || !value.TryGetProperty(name.GetString()!, out _))
                {
                    return false;
                }
            }
        }

        if (schema.TryGetProperty("additionalProperties", out var additional)
            && additional.ValueKind == JsonValueKind.False)
        {
            var known = schema.TryGetProperty("properties", out var properties)
                && properties.ValueKind == JsonValueKind.Object
                    ? properties.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal)
                    : [];
            foreach (var property in value.EnumerateObject())
            {
                if (!known.Contains(property.Name))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
