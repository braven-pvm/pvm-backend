using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pvm.Application.Banking;
using Pvm.Infrastructure.Acumatica;
using Xunit;

namespace Pvm.Infrastructure.Tests.Acumatica;

public sealed class AcumaticaBankStatementClientTests
{
    private const string CreatedJson = """{ "ReferenceNbr": { "value": "STMT000042" } }""";

    [Fact]
    public async Task ImportAsync_imports_new_lines_when_none_exist()
    {
        using var handler = new SequenceHandler(
            _ => new HttpResponseMessage(HttpStatusCode.NoContent), // sign-in
            _ => JsonResponse("[]"),                                // existing-id lookup: none
            _ => JsonResponse(CreatedJson),                         // PUT
            _ => new HttpResponseMessage(HttpStatusCode.NoContent)); // sign-out
        var client = NewClient(handler);

        var result = await client.ImportAsync(TwoLineStatement(), CancellationToken.None);

        Assert.Equal("STMT000042", result.ReferenceNbr);
        Assert.Equal(2, result.LineCount);

        var put = Assert.Single(handler.Requests, request => request.Method == HttpMethod.Put);
        Assert.EndsWith(
            "/entity/PVMBankFeed/01.000.001/BankStatement",
            put.Uri.AbsolutePath,
            StringComparison.Ordinal);

        using var body = JsonDocument.Parse(put.Body!);
        var root = body.RootElement;
        Assert.Equal("INVESTEC", root.GetProperty("CashAccount").GetProperty("value").GetString());
        Assert.Equal(1070m, root.GetProperty("EndingBalance").GetProperty("value").GetDecimal());

        var details = root.GetProperty("Details");
        Assert.Equal(2, details.GetArrayLength());
        Assert.Equal("INV-ABC", details[0].GetProperty("ExtTranID").GetProperty("value").GetString());
        Assert.Equal(100m, details[0].GetProperty("Receipt").GetProperty("value").GetDecimal());
        Assert.Equal("REF1", details[0].GetProperty("ExtRefNbr").GetProperty("value").GetString());
        Assert.Equal(30m, details[1].GetProperty("Disbursement").GetProperty("value").GetDecimal());
    }

    [Fact]
    public async Task ImportAsync_skips_put_when_all_lines_already_imported()
    {
        const string existing =
            """[{"Details":[{"ExtTranID":{"value":"INV-ABC"},"TranDate":{"value":"2026-08-03"}},{"ExtTranID":{"value":"INV-DEF"},"TranDate":{"value":"2026-08-04"}}]}]""";
        using var handler = new SequenceHandler(
            _ => new HttpResponseMessage(HttpStatusCode.NoContent), // sign-in
            _ => JsonResponse(existing),                            // lookup: both already present
            _ => new HttpResponseMessage(HttpStatusCode.NoContent)); // sign-out
        var client = NewClient(handler);

        var result = await client.ImportAsync(TwoLineStatement(), CancellationToken.None);

        Assert.Equal(0, result.LineCount);
        Assert.DoesNotContain(handler.Requests, request => request.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task ImportAsync_imports_only_new_lines_when_some_already_imported()
    {
        const string existing =
            """[{"Details":[{"ExtTranID":{"value":"INV-ABC"},"TranDate":{"value":"2026-08-03"}}]}]""";
        using var handler = new SequenceHandler(
            _ => new HttpResponseMessage(HttpStatusCode.NoContent), // sign-in
            _ => JsonResponse(existing),                            // lookup: only INV-ABC present
            _ => JsonResponse(CreatedJson),                         // PUT
            _ => new HttpResponseMessage(HttpStatusCode.NoContent)); // sign-out
        var client = NewClient(handler);

        var result = await client.ImportAsync(TwoLineStatement(), CancellationToken.None);

        Assert.Equal(1, result.LineCount);
        var put = Assert.Single(handler.Requests, request => request.Method == HttpMethod.Put);
        using var body = JsonDocument.Parse(put.Body!);
        var details = body.RootElement.GetProperty("Details");
        Assert.Equal(1, details.GetArrayLength());
        Assert.Equal("INV-DEF", details[0].GetProperty("ExtTranID").GetProperty("value").GetString());
    }

    [Fact]
    public async Task ImportAsync_signs_in_and_signs_out()
    {
        using var handler = new SequenceHandler(
            _ => new HttpResponseMessage(HttpStatusCode.NoContent),   // sign-in
            _ => new HttpResponseMessage(HttpStatusCode.NoContent));  // sign-out (empty -> no lookup, no PUT)
        var client = NewClient(handler);

        await client.ImportAsync(EmptyStatement(), CancellationToken.None);

        Assert.Single(
            handler.Requests,
            request => request.Uri.AbsolutePath.EndsWith("/entity/auth/login", StringComparison.Ordinal));
        Assert.Single(
            handler.Requests,
            request => request.Uri.AbsolutePath.EndsWith("/entity/auth/logout", StringComparison.Ordinal));
    }

    private static AcumaticaBankStatementClient NewClient(HttpMessageHandler handler)
        => new(
            new HttpClient(handler),
            Options.Create(DefaultAcumaticaOptions()),
            Options.Create(new AcumaticaBankFeedOptions()));

    private static BankStatementImport TwoLineStatement()
        => new(
            CashAccount: "INVESTEC",
            StatementDate: new DateOnly(2026, 8, 31),
            StartBalanceDate: new DateOnly(2026, 8, 1),
            EndBalanceDate: new DateOnly(2026, 8, 31),
            BeginningBalance: 1000m,
            EndingBalance: 1070m,
            Lines: new[]
            {
                new BankStatementLine("INV-ABC", new DateOnly(2026, 8, 3), "DEPOSIT", 100m, 0m, ExtRefNbr: "REF1"),
                new BankStatementLine("INV-DEF", new DateOnly(2026, 8, 4), "FEE", 0m, 30m),
            });

    private static BankStatementImport EmptyStatement()
        => new(
            CashAccount: "INVESTEC",
            StatementDate: new DateOnly(2026, 8, 31),
            StartBalanceDate: new DateOnly(2026, 8, 1),
            EndBalanceDate: new DateOnly(2026, 8, 31),
            BeginningBalance: 0m,
            EndingBalance: 0m,
            Lines: Array.Empty<BankStatementLine>());

    private static AcumaticaOptions DefaultAcumaticaOptions()
        => new()
        {
            BaseUrl = "https://acumatica.example/PVMGroup25R1",
            Company = "PVM Group",
            Branch = "MAIN",
            Username = "integration-user",
            Password = "secret",
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

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, body));
            return _responses.Dequeue()(request);
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? Body);
}
