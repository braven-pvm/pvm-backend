namespace Pvm.Infrastructure.Operations;

/// <summary>
/// A trading partner or ERP refused our credentials. This is never transient: retries cannot
/// fix it, and a person must renew the credentials. The run records a distinct error code and
/// the log line drives an hourly alert until the credentials work again.
/// </summary>
public sealed class IntegrationCredentialsRejectedException(string system, int statusCode)
    : InvalidOperationException($"{system} rejected the integration credentials (HTTP {statusCode}).")
{
    public const string LogEvent = "integration.credentials.rejected";

    public string System { get; } = system;

    public int StatusCode { get; } = statusCode;

    public static bool IsCredentialStatus(int statusCode) => statusCode is 401 or 403;
}
