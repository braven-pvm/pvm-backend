using System.Text.Json;
using System.Net;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pvm.Application.Documents;

namespace Pvm.Infrastructure.Documents;

/// <summary>
/// Sends a scanned PDF to Claude with the document type's field schema as structured output
/// (design 6.4). Content problems return <see cref="Unreadable"/>, and so does a request the API
/// rejects for its content (400, 413), which a retry cannot fix. Transport failures (network,
/// 401/403, 429, 5xx after the SDK's retries) throw so the queue message is retried.
/// </summary>
public sealed class ClaudeDocumentExtractor : IDocumentExtractor
{
    private const int MaxTokens = 4000;
    private const string FallbackBeta = "server-side-fallback-2026-07-01";

    /// <summary>Base64 characters above which the request would pass the API's 32 MB limit.</summary>
    private const long MaxBase64Length = 31_000_000;

    private readonly AnthropicOptions _options;
    private readonly ILogger _logger;
    private readonly HttpClient? _httpClient;
    private readonly int? _maxRetries;
    private readonly Lazy<IAnthropicClient> _client;

    public ClaudeDocumentExtractor(
        IOptions<AnthropicOptions> options,
        ILogger<ClaudeDocumentExtractor> logger)
        : this(options, logger, httpClient: null, maxRetries: null)
    {
    }

    /// <param name="httpClient">The HTTP client the SDK uses; null for the SDK default.</param>
    /// <param name="maxRetries">The SDK's retry count; null for the SDK default.</param>
    public ClaudeDocumentExtractor(
        IOptions<AnthropicOptions> options,
        ILogger<ClaudeDocumentExtractor>? logger,
        HttpClient? httpClient,
        int? maxRetries)
    {
        _options = options.Value;
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _httpClient = httpClient;
        _maxRetries = maxRetries;
        _client = new Lazy<IAnthropicClient>(CreateClient);
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
        if ((buffer.Length + 2) / 3 * 4 > MaxBase64Length)
        {
            return new Unreadable("too_large");
        }

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

        BetaMessage response;
        try
        {
            response = await client.Beta.Messages.Create(parameters, cancellationToken);
        }
        catch (AnthropicApiException ex)
            when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge)
        {
            _logger.LogWarning(
                "Claude rejected the {DocumentType} extraction request: HTTP {StatusCode}, {ErrorType}.",
                type.Code,
                (int)ex.StatusCode,
                ex.Type?.ToString() ?? "unknown");
            return new Unreadable("rejected");
        }

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

        if (!MatchesTopLevelShape(fields, type.FieldSchema))
        {
            return new Unreadable("schema");
        }

        return HasAnyValue(fields) ? new Extracted(fields) : new Unreadable("empty");
    }

    private IAnthropicClient CreateClient()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "Anthropic:ApiKey is not configured (Key Vault secret Anthropic--ApiKey); "
                + "document extraction cannot run.");
        }

        IAnthropicClient client = _httpClient is null
            ? new AnthropicClient { ApiKey = _options.ApiKey }
            : new AnthropicClient { ApiKey = _options.ApiKey, HttpClient = _httpClient };
        return _maxRetries is { } retries
            ? client.WithOptions(o => o with { MaxRetries = retries })
            : client;
    }

    /// <summary>
    /// True when any field holds a value: a non-empty list, a non-blank string, a number, or an
    /// object with such a field. Nulls, empty lists and booleans do not count.
    /// </summary>
    private static bool HasAnyValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().Any(p => HasAnyValue(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().Any(HasAnyValue),
        JsonValueKind.String => !string.IsNullOrWhiteSpace(element.GetString()),
        JsonValueKind.Number => true,
        _ => false,
    };

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
