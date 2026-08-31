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

        return await WithSessionAsync(async sessionCookie =>
        {
            // Acumatica does NOT de-duplicate the custom endpoint import on Ext. Tran. ID, so we
            // drop lines already present for this cash account. An overlapping re-pull or a
            // re-uploaded file therefore imports no duplicates, and no-ops when nothing is new.
            var newLines = await FilterAlreadyImportedAsync(statement, sessionCookie, cancellationToken);
            if (newLines.Count == 0)
            {
                return new BankStatementImportResult(string.Empty, 0);
            }

            var payload = BuildStatementPayload(statement, newLines);
            using var request = CreateSessionRequest(HttpMethod.Put, BuildEntityUri(), sessionCookie);
            request.Content = new StringContent(
                payload.ToJsonString(SerializerOptions),
                Encoding.UTF8,
                "application/json");
            using var response = await httpClient.SendAsync(request, cancellationToken);
            EnsureSuccess(response, "bank statement import");

            var reference = await ReadReferenceNbrAsync(response, cancellationToken);
            return new BankStatementImportResult(reference, newLines.Count);
        }, cancellationToken);
    }

    private async Task<IReadOnlyList<BankStatementLine>> FilterAlreadyImportedAsync(
        BankStatementImport statement,
        string? sessionCookie,
        CancellationToken cancellationToken)
    {
        if (statement.Lines.Count == 0)
        {
            return statement.Lines;
        }

        var minDate = statement.Lines.Min(line => line.TranDate);
        var maxDate = statement.Lines.Max(line => line.TranDate);
        var existing = await FetchExistingExtTranIdsAsync(
            statement.CashAccount, minDate, maxDate, sessionCookie, cancellationToken);
        if (existing.Count == 0)
        {
            return statement.Lines;
        }

        return statement.Lines.Where(line => !existing.Contains(line.ExtTranId)).ToList();
    }

    // Recent statements newest-first cover any realistic import window; the endpoint rejects
    // server-side date filters (HTTP 500), so the window is applied client-side below.
    private const int ExistingLookupStatementLimit = 100;

    private async Task<HashSet<string>> FetchExistingExtTranIdsAsync(
        string cashAccount,
        DateOnly minDate,
        DateOnly maxDate,
        string? sessionCookie,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        var query =
            $"?$filter={Uri.EscapeDataString($"CashAccount eq '{cashAccount}'")}" +
            "&$orderby=EndBalanceDate desc&$expand=Details" +
            $"&$top={ExistingLookupStatementLimit.ToString(CultureInfo.InvariantCulture)}";
        var uri = BuildUri(
            $"entity/{_bankFeed.EndpointName}/{_bankFeed.EndpointVersion}/{_bankFeed.TopLevelEntity}{query}");

        using var request = CreateSessionRequest(HttpMethod.Get, uri, sessionCookie);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Fail open: never block an import because the de-duplication lookup failed.
            return ids;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return ids;
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return ids;
        }

        foreach (var existingStatement in document.RootElement.EnumerateArray())
        {
            if (!existingStatement.TryGetProperty("Details", out var lines)
                || lines.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var line in lines.EnumerateArray())
            {
                var extTranId = ValueString(line, "ExtTranID");
                if (string.IsNullOrEmpty(extTranId))
                {
                    continue;
                }

                // Ext. Tran. IDs are unique per transaction; only lines in the import window collide.
                var tranDate = ValueString(line, "TranDate");
                if (DateOnly.TryParse(tranDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    && (date < minDate || date > maxDate))
                {
                    continue;
                }

                ids.Add(extTranId);
            }
        }

        return ids;
    }

    private static string? ValueString(JsonElement element, string name)
        => element.TryGetProperty(name, out var field)
            && field.ValueKind == JsonValueKind.Object
            && field.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static JsonObject BuildStatementPayload(
        BankStatementImport statement,
        IReadOnlyList<BankStatementLine> lines)
    {
        var details = new JsonArray();
        foreach (var line in lines)
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

        // Re-derive the beginning balance for the (possibly filtered) line set so that
        // beginning + net == ending stays consistent when duplicate lines were skipped.
        var net = lines.Sum(line => line.Receipt - line.Disbursement);

        return new JsonObject
        {
            ["CashAccount"] = Field(statement.CashAccount),
            ["StatementDate"] = Field(FormatDate(statement.StatementDate)),
            ["StartBalanceDate"] = Field(FormatDate(statement.StartBalanceDate)),
            ["EndBalanceDate"] = Field(FormatDate(statement.EndBalanceDate)),
            ["BeginningBalance"] = Field(statement.EndingBalance - net),
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
