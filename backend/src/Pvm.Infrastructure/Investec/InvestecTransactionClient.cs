using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pvm.Application.Investec;

namespace Pvm.Infrastructure.Investec;

/// <summary>
/// Investec BCB Transaction History client. Acquires an OAuth 2.0 client-credentials token,
/// then walks the paged <c>GET /za/bb/v2/accounts/{accountId}/transactions</c> endpoint and
/// maps each row (positive <c>amount</c> + <c>type</c> direction) to an <see cref="InvestecTransaction"/>.
/// </summary>
public sealed class InvestecTransactionClient(
    HttpClient httpClient,
    IOptions<InvestecOptions> options) : IInvestecTransactionClient
{
    private readonly InvestecOptions _options = options.Value;

    public async Task<IReadOnlyList<InvestecTransaction>> GetTransactionsAsync(
        string accountId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ValidateOptions();

        var token = await GetAccessTokenAsync(cancellationToken);
        var transactions = new List<InvestecTransaction>();

        for (var page = 1; page <= _options.MaxPages; page++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                BuildTransactionsUri(accountId, fromDate, toDate, page));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            EnsureSuccess(response, "transaction retrieval");

            var batch = await ParseTransactionsAsync(accountId, response, cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            transactions.AddRange(batch);
        }

        return transactions;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var basic = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("identity/v2/oauth2/token"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.TryAddWithoutValidation("x-api-key", _options.ApiKey);
        }

        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        EnsureSuccess(response, "token acquisition");

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("access_token", out var token)
            && token.ValueKind == JsonValueKind.String)
        {
            return token.GetString()!;
        }

        throw new InvalidOperationException("Investec token response did not contain an access_token.");
    }

    private static async Task<List<InvestecTransaction>> ParseTransactionsAsync(
        string accountId,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var transactions = new List<InvestecTransaction>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return transactions;
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("transactions", out var rows)
            && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                transactions.Add(MapTransaction(accountId, row));
            }
        }

        return transactions;
    }

    private static InvestecTransaction MapTransaction(string accountId, JsonElement row)
    {
        string? Text(string name) =>
            row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        decimal Amount(string name) =>
            row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetDecimal()
                : 0m;

        decimal? AmountOrNull(string name) =>
            row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetDecimal()
                : null;

        DateOnly Date(string name) =>
            DateOnly.TryParse(Text(name), CultureInfo.InvariantCulture, out var date) ? date : default;

        DateOnly? DateOrNull(string name) =>
            DateOnly.TryParse(Text(name), CultureInfo.InvariantCulture, out var date) ? date : null;

        return new InvestecTransaction(
            AccountId: Text("accountId") ?? accountId,
            Description: Text("description") ?? string.Empty,
            Amount: Amount("amount"),
            TransactionDate: Date("transactionDate"),
            Direction: Text("type"),
            TransactionType: Text("transactionType"),
            Status: Text("status"),
            CardNumber: Text("cardNumber"),
            PostingDate: DateOrNull("postingDate"),
            ValueDate: DateOrNull("valueDate"),
            RunningBalance: AmountOrNull("runningBalance"),
            Reference: Text("reference"));
    }

    private Uri BuildTransactionsUri(string accountId, DateOnly fromDate, DateOnly toDate, int page)
    {
        var query =
            $"?fromDate={fromDate:yyyy-MM-dd}&toDate={toDate:yyyy-MM-dd}&page={page.ToString(CultureInfo.InvariantCulture)}";
        return BuildUri($"za/bb/v2/accounts/{Uri.EscapeDataString(accountId)}/transactions{query}");
    }

    private Uri BuildUri(string relativePath)
        => new(new Uri(_options.BaseUrl!.TrimEnd('/') + "/"), relativePath);

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new InvalidOperationException("Investec BaseUrl is required.");
        }

        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new InvalidOperationException("Investec client credentials are required.");
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Investec {operation} failed with status {(int)response.StatusCode} ({response.StatusCode}).");
        }
    }
}
