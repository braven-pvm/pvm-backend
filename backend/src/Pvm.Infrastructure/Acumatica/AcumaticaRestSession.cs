using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pvm.Infrastructure.Operations;

namespace Pvm.Infrastructure.Acumatica;

/// <summary>
/// The parts of a call to the Acumatica contract-based REST API that every client shares: sign-in
/// and sign-out around one operation, the session cookie (kept only for the configured server),
/// the URIs, and the error rule. It holds no state between operations.
/// </summary>
internal sealed class AcumaticaRestSession(HttpClient httpClient, AcumaticaOptions options)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Signs in, runs the operation with the session cookie, and always signs out.</summary>
    public async Task<T> RunAsync<T>(
        Func<string?, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var login = new Dictionary<string, string>
        {
            ["name"] = options.Username!,
            ["password"] = options.Password!
        };
        if (!string.IsNullOrWhiteSpace(options.Company))
        {
            login["company"] = options.Company;
        }

        if (!string.IsNullOrWhiteSpace(options.Branch))
        {
            login["branch"] = options.Branch;
        }

        using var loginResponse = await httpClient.PostAsJsonAsync(
            BuildUri("entity/auth/login"),
            login,
            SerializerOptions,
            cancellationToken);
        EnsureSuccess(loginResponse, "sign-in");
        var sessionCookie = ReadSessionCookie(loginResponse);

        try
        {
            return await operation(sessionCookie);
        }
        finally
        {
            using var logoutRequest = CreateRequest(
                HttpMethod.Post,
                BuildUri("entity/auth/logout"),
                sessionCookie);
            using var logoutResponse = await httpClient.SendAsync(logoutRequest, CancellationToken.None);
            EnsureSuccess(logoutResponse, "sign-out");
        }
    }

    /// <summary>
    /// The cookies of the sign-in response, as a Cookie header for the configured server only.
    /// A cookie the response sets for another host is dropped, so the session never goes elsewhere.
    /// </summary>
    public string? ReadSessionCookie(HttpResponseMessage loginResponse)
    {
        if (!loginResponse.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders))
        {
            return null;
        }

        var baseUri = new Uri(options.BaseUrl!.TrimEnd('/') + "/");
        var cookies = new CookieContainer();
        foreach (var setCookie in setCookieHeaders)
        {
            cookies.SetCookies(baseUri, setCookie);
        }

        var value = cookies.GetCookieHeader(baseUri);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static HttpRequestMessage CreateRequest(
        HttpMethod method,
        Uri uri,
        string? sessionCookie)
    {
        var request = new HttpRequestMessage(method, uri);
        if (!string.IsNullOrWhiteSpace(sessionCookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", sessionCookie);
        }

        return request;
    }

    public Uri BuildUri(string relativePath)
        => new(new Uri(options.BaseUrl!.TrimEnd('/') + "/"), relativePath);

    public string BuildEntityEndpoint(string entity)
        => $"entity/{Uri.EscapeDataString(options.EndpointName)}/{Uri.EscapeDataString(options.EndpointVersion)}/{entity}";

    public static string EscapeFilterValue(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    public static void EnsureSuccess(HttpResponseMessage response, string operation)
    {
        if (operation == "sign-in"
            && IntegrationCredentialsRejectedException.IsCredentialStatus((int)response.StatusCode))
        {
            throw new IntegrationCredentialsRejectedException(
                "Acumatica",
                (int)response.StatusCode);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Acumatica {operation} returned HTTP {(int)response.StatusCode}.");
        }
    }

    public static async Task<JsonElement[]> ReadArrayAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Acumatica {operation} returned an unexpected payload shape.");
        }

        return document.RootElement.EnumerateArray().Select(element => element.Clone()).ToArray();
    }

    /// <summary>The settings every call needs: an HTTPS server, a user, and the endpoint.</summary>
    public static void ValidateConnection(AcumaticaOptions options)
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Acumatica:BaseUrl must be a non-empty absolute HTTPS URI.");
        }

        Require(options.Username, "Username");
        Require(options.Password, "Password");
        Require(options.EndpointName, "EndpointName");
        Require(options.EndpointVersion, "EndpointVersion");
    }

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Acumatica:{name} is required.");
        }
    }
}
