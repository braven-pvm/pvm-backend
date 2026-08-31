using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Pvm.Application.Banking;

namespace Pvm.Infrastructure.Acumatica;

/// <summary>
/// Writes a <see cref="BankStatementImport"/> to Acumatica through the custom
/// <c>PVMBankFeed</c> endpoint. Mirrors <see cref="AcumaticaInvoiceClient"/>: an explicit
/// sign-in/sign-out session with a manually managed session cookie (the HttpClient handler
/// is configured cookie-less).
/// </summary>
public sealed class AcumaticaBankStatementClient(
    HttpClient httpClient,
    IOptions<AcumaticaOptions> acumaticaOptions,
    IOptions<AcumaticaBankFeedOptions> bankFeedOptions) : IAcumaticaBankStatementClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly AcumaticaOptions _acumatica = acumaticaOptions.Value;
    private readonly AcumaticaBankFeedOptions _bankFeed = bankFeedOptions.Value;

    public async Task<BankStatementImportResult> ImportAsync(
        BankStatementImport statement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ValidateOptions();

        var payload = BuildStatementPayload(statement);

        return await WithSessionAsync(async sessionCookie =>
        {
            using var request = CreateSessionRequest(HttpMethod.Put, BuildEntityUri(), sessionCookie);
            request.Content = new StringContent(
                payload.ToJsonString(SerializerOptions),
                Encoding.UTF8,
                "application/json");
            using var response = await httpClient.SendAsync(request, cancellationToken);
            EnsureSuccess(response, "bank statement import");

            var reference = await ReadReferenceNbrAsync(response, cancellationToken);
            return new BankStatementImportResult(reference, statement.Lines.Count);
        }, cancellationToken);
    }

    private static JsonObject BuildStatementPayload(BankStatementImport statement)
    {
        var details = new JsonArray();
        foreach (var line in statement.Lines)
        {
            var detail = new JsonObject
            {
                ["ExtTranID"] = Field(line.ExtTranId),
                ["TranDate"] = Field(FormatDate(line.TranDate)),
                ["TranDesc"] = Field(line.Description),
                ["Receipt"] = Field(line.Receipt),
                ["Disbursement"] = Field(line.Disbursement),
            };
            if (line.ExtRefNbr is not null)
            {
                detail["ExtRefNbr"] = Field(line.ExtRefNbr);
            }

            if (line.CardNumber is not null)
            {
                detail["CardNumber"] = Field(line.CardNumber);
            }

            details.Add(detail);
        }

        return new JsonObject
        {
            ["CashAccount"] = Field(statement.CashAccount),
            ["StatementDate"] = Field(FormatDate(statement.StatementDate)),
            ["StartBalanceDate"] = Field(FormatDate(statement.StartBalanceDate)),
            ["EndBalanceDate"] = Field(FormatDate(statement.EndBalanceDate)),
            ["BeginningBalance"] = Field(statement.BeginningBalance),
            ["EndingBalance"] = Field(statement.EndingBalance),
            ["Details"] = details,
        };
    }

    private async Task<T> WithSessionAsync<T>(
        Func<string?, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var login = new Dictionary<string, string>
        {
            ["name"] = _acumatica.Username!,
            ["password"] = _acumatica.Password!,
        };
        if (!string.IsNullOrWhiteSpace(_acumatica.Company))
        {
            login["company"] = _acumatica.Company;
        }

        if (!string.IsNullOrWhiteSpace(_acumatica.Branch))
        {
            login["branch"] = _acumatica.Branch;
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
            using var logoutRequest = CreateSessionRequest(
                HttpMethod.Post,
                BuildUri("entity/auth/logout"),
                sessionCookie);
            using var logoutResponse = await httpClient.SendAsync(logoutRequest, CancellationToken.None);
            EnsureSuccess(logoutResponse, "sign-out");
        }
    }

    private static async Task<string> ReadReferenceNbrAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("ReferenceNbr", out var reference)
            && reference.ValueKind == JsonValueKind.Object
            && reference.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private string? ReadSessionCookie(HttpResponseMessage loginResponse)
    {
        if (!loginResponse.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders))
        {
            return null;
        }

        var baseUri = new Uri(_acumatica.BaseUrl!.TrimEnd('/') + "/");
        var cookies = new CookieContainer();
        foreach (var setCookie in setCookieHeaders)
        {
            cookies.SetCookies(baseUri, setCookie);
        }

        var value = cookies.GetCookieHeader(baseUri);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static HttpRequestMessage CreateSessionRequest(HttpMethod method, Uri uri, string? sessionCookie)
    {
        var request = new HttpRequestMessage(method, uri);
        if (!string.IsNullOrWhiteSpace(sessionCookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", sessionCookie);
        }

        return request;
    }

    private Uri BuildEntityUri() => BuildUri(
        $"entity/{_bankFeed.EndpointName}/{_bankFeed.EndpointVersion}/{_bankFeed.TopLevelEntity}");

    private Uri BuildUri(string relativePath)
        => new(new Uri(_acumatica.BaseUrl!.TrimEnd('/') + "/"), relativePath);

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_acumatica.BaseUrl))
        {
            throw new InvalidOperationException("Acumatica BaseUrl is required for the bank statement import.");
        }

        if (string.IsNullOrWhiteSpace(_acumatica.Username) || string.IsNullOrWhiteSpace(_acumatica.Password))
        {
            throw new InvalidOperationException("Acumatica credentials are required for the bank statement import.");
        }

        if (string.IsNullOrWhiteSpace(_bankFeed.EndpointName) || string.IsNullOrWhiteSpace(_bankFeed.EndpointVersion))
        {
            throw new InvalidOperationException("Acumatica bank feed endpoint name and version are required.");
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Acumatica {operation} failed with status {(int)response.StatusCode} ({response.StatusCode}).");
        }
    }

    private static JsonObject Field(string value) => new() { ["value"] = value };

    private static JsonObject Field(decimal value) => new() { ["value"] = value };

    private static string FormatDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
