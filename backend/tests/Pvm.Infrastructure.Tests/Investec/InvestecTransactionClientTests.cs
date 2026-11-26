using System.Net;
using Microsoft.Extensions.Options;
using Pvm.Infrastructure.Investec;
using Xunit;

namespace Pvm.Infrastructure.Tests.Investec;

public sealed class InvestecTransactionClientTests
{
    [Fact]
    public async Task GetTransactionsAsync_authenticates_maps_direction_and_pages_to_empty()
    {
        const string tokenJson = """{"access_token":"tok-123","token_type":"Bearer","expires_in":1799}""";
        const string page1 = """
            {"data":{"transactions":[
              {"accountId":"1300","transactionId":"202608030000000000001","drCrIndicator":"CREDIT",
               "transactionDescription":"EFT Transaction","clientStatementReference":"SALARY AUG",
               "postDate":"2026-08-03","valueDate":"2026-08-03","deposit":1000.00,"withdrawal":0.00,
               "runningBalance":5000.00,"transactionStatus":"approved","transactionCode":"607"},
              {"accountId":"1300","transactionId":"202608040000000000002","drCrIndicator":"DEBIT",
               "transactionDescription":"EFT Transaction","clientStatementReference":"BANK FEE",
               "postDate":"2026-08-04","valueDate":"2026-08-04","deposit":0.00,"withdrawal":-55.00,
               "runningBalance":4945.00,"transactionStatus":"approved","transactionCode":"247"}
            ]},"meta":{"TotalPages":1,"CurrentPage":1}}
            """;
        const string page2 = """{"data":{"transactions":[]}}""";
        using var handler = new SequenceHandler(
            _ => JsonResponse(tokenJson),
            _ => JsonResponse(page1),
            _ => JsonResponse(page2));
        using var httpClient = new HttpClient(handler);
        var client = new InvestecTransactionClient(httpClient, Options.Create(DefaultOptions()));

        var transactions = await client.GetTransactionsAsync(
            "1300",
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31),
            CancellationToken.None);

        Assert.Equal(2, transactions.Count);
        var credit = transactions[0];
        Assert.Equal("CREDIT", credit.Direction);
        Assert.Equal(1000.00m, credit.Amount);
        Assert.Equal(5000.00m, credit.RunningBalance);
        Assert.Equal(new DateOnly(2026, 8, 3), credit.PostingDate);
        Assert.Equal("SALARY AUG", credit.Description);
        Assert.Equal("202608030000000000001", credit.TransactionId);
        Assert.Equal("BANK FEE", transactions[1].Description);
        Assert.Equal("DEBIT", transactions[1].Direction);
        Assert.Equal(55.00m, transactions[1].Amount);

        var token = Assert.Single(
            handler.Requests,
            request => request.Uri.AbsolutePath.EndsWith("/identity/v2/oauth2/token", StringComparison.Ordinal));
        Assert.Equal(HttpMethod.Post, token.Method);
        Assert.StartsWith("Basic ", token.Authorization);
        Assert.Equal("test-api-key", token.ApiKey);

        var txnCall = handler.Requests.First(
            request => request.Uri.AbsolutePath.Contains("/transactions", StringComparison.Ordinal));
        Assert.Equal("Bearer tok-123", txnCall.Authorization);
        Assert.Contains("fromDate=2026-08-01", txnCall.Uri.Query);
        Assert.Contains("toDate=2026-08-31", txnCall.Uri.Query);
    }

    [Fact]
    public async Task GetTransactionsAsync_recovers_from_a_short_401_with_a_fresh_token()
    {
        // 26 Nov 2026: Investec issued a token, then refused the transactions call with 401 for
        // about a minute. One refusal must not cost the whole day's import.
        using var handler = new SequenceHandler(
            _ => JsonResponse("""{"access_token":"tok-1"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            _ => JsonResponse("""{"access_token":"tok-2"}"""),
            _ => JsonResponse(OneTransactionPage),
            _ => JsonResponse("""{"data":{"transactions":[]}}"""));
        using var httpClient = new HttpClient(handler);
        var client = new InvestecTransactionClient(httpClient, Options.Create(DefaultOptions()));

        var transactions = await client.GetTransactionsAsync(
            "1300", new DateOnly(2026, 11, 19), new DateOnly(2026, 11, 26), CancellationToken.None);

        Assert.Single(transactions);
        var transactionCalls = handler.Requests
            .Where(request => request.Uri.AbsolutePath.Contains("/transactions", StringComparison.Ordinal))
            .ToList();
        Assert.Equal("Bearer tok-1", transactionCalls[0].Authorization);
        Assert.Equal("Bearer tok-2", transactionCalls[1].Authorization);
        Assert.Contains("page=1", transactionCalls[1].Uri.Query);
    }

    [Fact]
    public async Task GetTransactionsAsync_still_fails_when_the_401_persists()
    {
        // Real credential trouble must still surface as a failed run, not loop forever.
        var responses = new List<Func<HttpRequestMessage, HttpResponseMessage>>();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            responses.Add(_ => JsonResponse("""{"access_token":"tok"}"""));
            responses.Add(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }

        using var handler = new SequenceHandler([.. responses]);
        using var httpClient = new HttpClient(handler);
        var client = new InvestecTransactionClient(httpClient, Options.Create(DefaultOptions()));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetTransactionsAsync(
            "1300", new DateOnly(2026, 11, 19), new DateOnly(2026, 11, 26), CancellationToken.None));

        Assert.Contains("401", error.Message, StringComparison.Ordinal);
        Assert.Equal(4, handler.Requests.Count(request =>
            request.Uri.AbsolutePath.EndsWith("/identity/v2/oauth2/token", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task GetTransactionsAsync_does_not_retry_other_failures()
    {
        using var handler = new SequenceHandler(
            _ => JsonResponse("""{"access_token":"tok"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var httpClient = new HttpClient(handler);
        var client = new InvestecTransactionClient(httpClient, Options.Create(DefaultOptions()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetTransactionsAsync(
            "1300", new DateOnly(2026, 11, 19), new DateOnly(2026, 11, 26), CancellationToken.None));

        Assert.Equal(2, handler.Requests.Count);
    }

    private const string OneTransactionPage = """
        {"data":{"transactions":[
          {"accountId":"1300","transactionId":"202611250000000000001","drCrIndicator":"CREDIT",
           "postDate":"2026-11-25","valueDate":"2026-11-25","deposit":10.00,"withdrawal":0.00}
        ]}}
        """;

    private static InvestecOptions DefaultOptions()
        => new()
        {
            BaseUrl = "https://openapisandbox.investec.example",
            ClientId = "cid",
            ClientSecret = "secret",
            ApiKey = "test-api-key",
            AccountId = "1300",
            UnauthorizedRetryDelaySeconds = 0,
        };

    private static HttpResponseMessage JsonResponse(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

    private sealed class SequenceHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new(responses);

        public List<CapturedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var authorization = request.Headers.Authorization is { } header
                ? $"{header.Scheme} {header.Parameter}"
                : null;
            var apiKey = request.Headers.TryGetValues("x-api-key", out var values)
                ? string.Join(",", values)
                : null;
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, authorization, apiKey));
            return Task.FromResult(_responses.Dequeue()(request));
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? ApiKey);
}
