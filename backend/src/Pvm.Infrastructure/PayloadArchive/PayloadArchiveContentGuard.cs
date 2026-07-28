using System.Text.RegularExpressions;

namespace Pvm.Infrastructure.PayloadArchive;

internal static partial class PayloadArchiveContentGuard
{
    public static void EnsureSafe(string content)
    {
        if (CredentialBearingUri().IsMatch(content)
            || SecretJsonProperty().IsMatch(content)
            || SecretHeader().IsMatch(content)
            || SecretQueryParameter().IsMatch(content))
        {
            throw new InvalidOperationException(
                "Payload archive content contains credential-shaped data and was rejected.");
        }
    }

    [GeneratedRegex(@"https?://[^\s/:@]+:[^\s/@]+@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CredentialBearingUri();

    [GeneratedRegex(
        @"""(?:password|client_secret|access_token|refresh_token|authorization|cookie)""\s*:",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretJsonProperty();

    [GeneratedRegex(
        @"(?:^|\r?\n)\s*(?:authorization|cookie|set-cookie)\s*:",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretHeader();

    [GeneratedRegex(
        @"[?&](?:password|client_secret|access_token|refresh_token|authorization)=",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretQueryParameter();
}
