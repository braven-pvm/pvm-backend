using System.Text.RegularExpressions;

namespace Pvm.Infrastructure.Messaging;

internal static partial class DiagnosticSummary
{
    public static string? Safe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var redacted = CredentialUri().Replace(value, "$1[REDACTED]@");
        redacted = NamedValue().Replace(redacted, "$1=[REDACTED]");
        redacted = JsonSecret().Replace(redacted, "$1\"[REDACTED]\"");
        return redacted[..Math.Min(redacted.Length, 1024)];
    }

    [GeneratedRegex(@"(https?://)[^\s/:@]+:[^\s/@]+@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CredentialUri();

    [GeneratedRegex(
        @"\b(password|username|client_secret|access_token|refresh_token|authorization|cookie)\s*=\s*[^&;\s]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedValue();

    [GeneratedRegex(
        "(\\\"(?:password|username|client_secret|access_token|refresh_token|authorization|cookie)\\\"\\s*:\\s*)\\\"[^\\\"]*\\\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JsonSecret();
}
