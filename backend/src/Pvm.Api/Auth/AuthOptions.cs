namespace Pvm.Api.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string Mode { get; init; } = "Entra";
    public string? TenantId { get; init; }
    public string? Audience { get; init; }
    public string[] BootstrapAdminEmails { get; init; } = [];
    public string[] BootstrapAdminObjectIds { get; init; } = [];

    public bool IsDevelopmentBypass =>
        string.Equals(Mode, "DevelopmentBypass", StringComparison.OrdinalIgnoreCase);
}
