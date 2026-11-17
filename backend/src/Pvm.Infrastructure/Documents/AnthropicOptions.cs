namespace Pvm.Infrastructure.Documents;

/// <summary>Claude API settings. <c>ApiKey</c> comes from Key Vault secret <c>Anthropic--ApiKey</c>.</summary>
public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "claude-opus-5-5";
}
