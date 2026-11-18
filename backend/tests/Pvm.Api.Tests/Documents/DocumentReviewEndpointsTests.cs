using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pvm.Api.Auth;
using Pvm.Api.Features.Documents;
using Pvm.Application.Documents;
using Pvm.Application.Messaging;
using Pvm.Infrastructure.Documents;
using Pvm.Infrastructure.Messaging;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Api.Tests.Documents;

/// <summary>
/// HTTP behaviour of the read and review endpoints, with real policies. The conditional state
/// writes behind file-to and reject run on Postgres in DocumentReviewServiceTests; EF InMemory
/// cannot run them, so these tests cover every path that answers before the write.
/// </summary>
public sealed class DocumentReviewEndpointsTests : IAsyncLifetime
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n% a scanned proof of delivery\n%%EOF\n");

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pvm-document-review-tests-{Guid.NewGuid():N}");
    private readonly string _databaseName = $"document-review-{Guid.NewGuid():N}";
    private readonly FakeAcumatica _acumatica = new();
    private WebApplication? _app;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Search_ByInvoice_ReturnsBothPodsForTheInvoice_NewestFirst()
    {
        var client = await StartAsync();
        var now = DateTimeOffset.UtcNow;
        var older = await SeedAsync(DocumentState.Filed, uploadedAt: now.AddDays(-2), matchedReference: "INV001234");
        var newer = await SeedAsync(DocumentState.Filed, uploadedAt: now.AddHours(-1), matchedReference: "INV001234");
        await SeedAsync(DocumentState.Filed, uploadedAt: now, matchedReference: "INV009999");
        await SeedAsync(DocumentState.NeedsReview, uploadedAt: now);

        var response = await client.SendAsync(Get("/api/documents?invoice=inv001234", AppRoles.Viewer));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([newer, older], items.Select(item => item.GetProperty("id").GetGuid()));
        Assert.Equal(2, body.GetProperty("total").GetInt32());
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(50, body.GetProperty("pageSize").GetInt32());
        var first = items[0];
        Assert.Equal("Filed", first.GetProperty("state").GetString());
        Assert.Equal("INV001234", first.GetProperty("matchedReference").GetString());
        Assert.Equal("Acme Stores (Pty) Ltd", first.GetProperty("matchedCustomerName").GetString());
        Assert.False(first.TryGetProperty("blobPath", out _));
    }

    [Fact]
    public async Task Search_Filters_ClientTypeStateAndDates()
    {
        var client = await StartAsync();
        var now = DateTimeOffset.UtcNow;
        var match = await SeedAsync(DocumentState.Filed, uploadedAt: now.AddDays(-3), matchedReference: "INV1");
        await SeedAsync(DocumentState.Filed, uploadedAt: now.AddDays(-10), matchedReference: "INV2");
        await SeedAsync(DocumentState.NeedsReview, uploadedAt: now.AddDays(-3));
        await SeedAsync(DocumentState.Filed, uploadedAt: now.AddDays(-3), matchedReference: "INV3", customerName: "Other Ltd");

        var from = Uri.EscapeDataString(now.AddDays(-5).ToString("O"));
        var to = Uri.EscapeDataString(now.AddDays(-1).ToString("O"));
        var response = await client.SendAsync(Get(
            $"/api/documents?client=acme&type=pod&state=filed&from={from}&to={to}",
            AppRoles.Viewer));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(match, item.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Search_UnknownState_Returns400()
    {
        var client = await StartAsync();

        var response = await client.SendAsync(Get("/api/documents?state=Lost", AppRoles.Viewer));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_PagesOf50()
    {
        var client = await StartAsync();
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 51; index++)
        {
            await SeedAsync(DocumentState.Filed, uploadedAt: now.AddMinutes(-index), matchedReference: "INV1");
        }

        var page2 = await client.SendAsync(Get("/api/documents?page=2", AppRoles.Viewer));

        var body = await page2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(51, body.GetProperty("total").GetInt32());
        Assert.Equal(2, body.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task ReviewList_OpenAndStuckRows_OldestFirst_WithoutRejectedFiledOrFreshRows()
    {
        var client = await StartAsync();
        var now = DateTimeOffset.UtcNow;
        var review = await SeedAsync(DocumentState.NeedsReview, uploadedAt: now.AddHours(-1), reviewReason: ReviewReason.NoSecondSignal);
        var failed = await SeedAsync(DocumentState.Failed, uploadedAt: now.AddHours(-3));
        var stuck = await SeedAsync(DocumentState.Processing, uploadedAt: now.AddMinutes(-31));
        await SeedAsync(DocumentState.Received, uploadedAt: now.AddMinutes(-5));
        await SeedAsync(DocumentState.Filed, uploadedAt: now.AddHours(-5), matchedReference: "INV1");
        await SeedAsync(DocumentState.NeedsReview, uploadedAt: now.AddHours(-6), rejectedAt: now.AddHours(-2));

        var response = await client.SendAsync(Get("/api/documents/review", AppRoles.Viewer));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([failed, review, stuck], items.Select(item => item.GetProperty("id").GetGuid()));
        Assert.Equal(3, body.GetProperty("total").GetInt32());
        Assert.False(items[0].GetProperty("stuck").GetBoolean());
        Assert.Equal("NoSecondSignal", items[1].GetProperty("reviewReason").GetString());
        Assert.True(items[2].GetProperty("stuck").GetBoolean());
        Assert.Equal("Processing", items[2].GetProperty("state").GetString());
    }

    [Fact]
    public async Task Detail_ReturnsRowExtractionAndCandidates_NotTheBlobPath()
    {
        var client = await StartAsync();
        var id = await SeedAsync(
            DocumentState.NeedsReview,
            reviewReason: ReviewReason.MultipleCandidates,
            extractionJson: """
                {"invoiceNumbers":["INV1"],"clientName":"Acme",
                 "candidates":[{"referenceNumber":"INV1","customerName":"Acme Stores (Pty) Ltd"},{"referenceNumber":"INV2","customerName":"Acme Stores (Pty) Ltd"}]}
                """);

        var response = await client.SendAsync(Get($"/api/documents/{id:D}", AppRoles.Viewer));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, body.GetProperty("id").GetGuid());
        Assert.Equal("NeedsReview", body.GetProperty("state").GetString());
        Assert.Equal("MultipleCandidates", body.GetProperty("reviewReason").GetString());
        Assert.Equal("POD", body.GetProperty("documentType").GetString());
        Assert.Equal("driver@pvm.test", body.GetProperty("uploadedBy").GetString());
        var extraction = body.GetProperty("extraction");
        Assert.Equal("Acme", extraction.GetProperty("clientName").GetString());
        Assert.False(extraction.TryGetProperty("candidates", out _));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("unreadableReason").ValueKind);
        var candidates = body.GetProperty("candidates").EnumerateArray().ToList();
        Assert.Equal(["INV1", "INV2"], candidates.Select(c => c.GetProperty("referenceNumber").GetString()));
        Assert.False(body.TryGetProperty("blobPath", out _));
    }

    [Fact]
    public async Task Detail_Unreadable_ReturnsReason_NoExtraction_EmptyCandidates()
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.NeedsReview, extractionJson: """{"unreadable":"blank-page"}""");

        var response = await client.SendAsync(Get($"/api/documents/{id:D}", AppRoles.Viewer));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("blank-page", body.GetProperty("unreadableReason").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("extraction").ValueKind);
        Assert.Empty(body.GetProperty("candidates").EnumerateArray());
    }

    [Theory]
    [InlineData(null, "Shadow")]
    [InlineData("Write", "Write")]
    public async Task Detail_ReturnsTheAcumaticaAttachMode(string? configured, string expected)
    {
        var client = await StartAsync(attachMode: configured);
        var id = await SeedAsync(DocumentState.NeedsReview);

        var response = await client.SendAsync(Get($"/api/documents/{id:D}", AppRoles.Viewer));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expected, body.GetProperty("acumaticaAttachMode").GetString());
    }

    [Fact]
    public async Task Detail_Unknown_Returns404()
    {
        var client = await StartAsync();

        var response = await client.SendAsync(Get($"/api/documents/{Guid.NewGuid():D}", AppRoles.Viewer));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task File_StreamsThePdf_Inline()
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.Filed, matchedReference: "INV1");

        var response = await client.SendAsync(Get($"/api/documents/{id:D}/file", AppRoles.Viewer));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal($"POD-{id:D}.pdf", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal(Pdf, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ReadEndpoints_Anonymous_Return401()
    {
        var client = await StartAsync();

        var response = await client.GetAsync("/api/documents");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InvoiceSearch_ReturnsTheAcumaticaResults()
    {
        var client = await StartAsync();
        _acumatica.SearchResults = [Invoice("INV001234")];

        var response = await client.SendAsync(Get("/api/documents/review/invoice-search?q=INV0012", AppRoles.Operator));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var invoice = Assert.Single(body.EnumerateArray());
        Assert.Equal("INV001234", invoice.GetProperty("referenceNumber").GetString());
        Assert.Equal("Acme Stores (Pty) Ltd", invoice.GetProperty("customerName").GetString());
        Assert.Equal(["INV0012"], _acumatica.Queries);
    }

    [Fact]
    public async Task InvoiceSearch_Viewer_Returns403()
    {
        var client = await StartAsync();

        var response = await client.SendAsync(Get("/api/documents/review/invoice-search?q=INV", AppRoles.Viewer));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FileTo_FiledDocument_Returns409()
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.Filed, matchedReference: "INV1");
        _acumatica.SearchResults = [Invoice("INV001234")];

        var response = await client.SendAsync(FileTo(id, "INV001234"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("INV1", (await LoadAsync(id)).MatchedReference);
    }

    [Fact]
    public async Task FileTo_InvoiceTheSearchCannotFind_Returns400()
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.NeedsReview);
        // A prefix hit is not the invoice: only an exact reference counts.
        _acumatica.SearchResults = [Invoice("INV0012345")];

        var response = await client.SendAsync(FileTo(id, "INV001234"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["INV001234"], _acumatica.Lookups);
        Assert.Empty(_acumatica.Queries);
        var row = await LoadAsync(id);
        Assert.Equal(DocumentState.NeedsReview, row.State);
        Assert.Null(row.MatchedReference);
    }

    [Fact]
    public async Task FileTo_Viewer_Returns403()
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.NeedsReview);

        var response = await client.SendAsync(FileTo(id, "INV001234", AppRoles.Viewer));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_acumatica.Lookups);
    }

    [Fact]
    public async Task FileTo_BlankReference_Returns400()
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.NeedsReview);

        var response = await client.SendAsync(FileTo(id, "  "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FileTo_UnknownDocument_Returns404()
    {
        var client = await StartAsync();

        var response = await client.SendAsync(FileTo(Guid.NewGuid(), "INV001234"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FileTo_UserNotResolved_Returns500_AndWritesNothing()
    {
        var client = await StartAsync(resolveUser: false);
        var id = await SeedAsync(DocumentState.NeedsReview);
        _acumatica.SearchResults = [Invoice("INV001234")];

        var response = await client.SendAsync(FileTo(id, "INV001234"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(DocumentState.NeedsReview, (await LoadAsync(id)).State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Reject_BlankNote_Returns400(string note)
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.NeedsReview);

        var response = await client.SendAsync(Reject(id, note));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reject_NoteOver1000_Returns400()
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.NeedsReview);

        var response = await client.SendAsync(Reject(id, new string('x', 1001)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reject_Viewer_Returns403()
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.NeedsReview);

        var response = await client.SendAsync(Reject(id, "bad scan", AppRoles.Viewer));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reject_FiledDocument_Returns409()
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.Filed, matchedReference: "INV1");

        var response = await client.SendAsync(Reject(id, "bad scan"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReturnToReview_BlankNote_Returns400(string note)
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.Filed, matchedReference: "INV1");

        var response = await client.SendAsync(ReturnToReview(id, note));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReturnToReview_NoteOver1000_Returns400()
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.Filed, matchedReference: "INV1");

        var response = await client.SendAsync(ReturnToReview(id, new string('x', 1001)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReturnToReview_Viewer_Returns403()
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.Filed, matchedReference: "INV1");

        var response = await client.SendAsync(ReturnToReview(id, "wrong invoice", AppRoles.Viewer));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReturnToReview_UnknownDocument_Returns404()
    {
        var client = await StartAsync();

        var response = await client.SendAsync(ReturnToReview(Guid.NewGuid(), "wrong invoice"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReturnToReview_NotFiled_Returns409()
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.NeedsReview);

        var response = await client.SendAsync(ReturnToReview(id, "wrong invoice"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ReturnToReview_AlreadyAttached_Returns409_SaysRemoveInAcumatica()
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.Filed, matchedReference: "INV1", acumaticaAttachedAt: DateTimeOffset.UtcNow);

        var response = await client.SendAsync(ReturnToReview(id, "wrong invoice"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var detail = problem.GetProperty("detail").GetString()!;
        Assert.Contains("already attached", detail);
        Assert.Contains("Acumatica", detail);
        Assert.Contains("by hand", detail);
        Assert.Equal(DocumentState.Filed, (await LoadAsync(id)).State);
    }

    [Fact]
    public async Task ProcessAgain_Viewer_Returns403()
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.Failed);

        var response = await client.SendAsync(ProcessAgain(id, AppRoles.Viewer));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ProcessAgain_UnknownDocument_Returns404()
    {
        var client = await StartAsync();

        var response = await client.SendAsync(ProcessAgain(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ProcessAgain_FiledDocument_Returns409()
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.Filed, matchedReference: "INV1");

        var response = await client.SendAsync(ProcessAgain(id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ProcessAgain_FreshProcessingDocument_Returns409()
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.Processing, uploadedAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        var response = await client.SendAsync(ProcessAgain(id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private static InvoiceCandidate Invoice(string reference)
        => new(reference, new DateOnly(2026, 11, 1), "C001", "Acme Stores (Pty) Ltd", null, null, "Open");

    private async Task<Guid> SeedAsync(
        DocumentState state,
        DateTimeOffset? uploadedAt = null,
        string? matchedReference = null,
        string customerName = "Acme Stores (Pty) Ltd",
        ReviewReason? reviewReason = null,
        string? extractionJson = null,
        DateTimeOffset? rejectedAt = null,
        DateTimeOffset? acumaticaAttachedAt = null)
    {
        var id = Guid.NewGuid();
        var at = uploadedAt ?? DateTimeOffset.UtcNow;
        var blobPath = $"documents/POD/{at:yyyy}/{at:MM}/{id:D}.pdf";
        await using var scope = _app!.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IDocumentStore>()
            .SaveAsync(blobPath, new MemoryStream(Pdf), CancellationToken.None);
        var db = scope.ServiceProvider.GetRequiredService<PvmDbContext>();
        db.CapturedDocuments.Add(new CapturedDocumentEntity
        {
            Id = id,
            ClientDocumentId = Guid.NewGuid(),
            DocumentType = "POD",
            State = state,
            ReviewReason = reviewReason,
            BlobPath = blobPath,
            PageCount = 1,
            SizeBytes = Pdf.Length,
            Sha256 = new string('a', 64),
            ExtractionJson = extractionJson,
            MatchedReference = matchedReference,
            MatchedCustomerName = matchedReference is null ? null : customerName,
            MatchedBy = matchedReference is null ? null : "auto",
            AcumaticaAttachedAt = acumaticaAttachedAt,
            UploadedBy = "driver@pvm.test",
            UploadedAt = at,
            UpdatedAt = at,
            RejectedAt = rejectedAt,
            RejectedBy = rejectedAt is null ? null : "operator@pvm.test",
            ReviewNote = rejectedAt is null ? null : "bad scan",
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<CapturedDocumentEntity> LoadAsync(Guid id)
    {
        await using var scope = _app!.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PvmDbContext>()
            .CapturedDocuments.AsNoTracking().SingleAsync(document => document.Id == id);
    }

    private static HttpRequestMessage Get(string url, string role)
        => new(HttpMethod.Get, url) { Headers = { { TestAuthHandler.RoleHeader, role } } };

    private static HttpRequestMessage FileTo(Guid id, string reference, string role = AppRoles.Operator)
        => new(HttpMethod.Post, $"/api/documents/{id:D}/file-to")
        {
            Content = JsonContent.Create(new { invoiceReference = reference }),
            Headers = { { TestAuthHandler.RoleHeader, role } }
        };

    private static HttpRequestMessage Reject(Guid id, string note, string role = AppRoles.Operator)
        => new(HttpMethod.Post, $"/api/documents/{id:D}/reject")
        {
            Content = JsonContent.Create(new { note }),
            Headers = { { TestAuthHandler.RoleHeader, role } }
        };

    private static HttpRequestMessage ReturnToReview(Guid id, string note, string role = AppRoles.Operator)
        => new(HttpMethod.Post, $"/api/documents/{id:D}/return-to-review")
        {
            Content = JsonContent.Create(new { note }),
            Headers = { { TestAuthHandler.RoleHeader, role } }
        };

    private static HttpRequestMessage ProcessAgain(Guid id, string role = AppRoles.Operator)
        => new(HttpMethod.Post, $"/api/documents/{id:D}/process-again")
        {
            Headers = { { TestAuthHandler.RoleHeader, role } }
        };

    private async Task<HttpClient> StartAsync(bool resolveUser = true, string? attachMode = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DocumentCapture:Provider"] = "FileSystem",
            ["DocumentCapture:FileSystemRoot"] = _root,
            ["DocumentCapture:AcumaticaAttachMode"] = attachMode
        });
        builder.Services.AddDbContext<PvmDbContext>(options => options
            .UseInMemoryDatabase(_databaseName)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        builder.Services.AddScoped<CurrentAppUserAccessor>();
        builder.Services.AddDocumentCapture(builder.Configuration);
        builder.Services.AddScoped<DocumentIntakeService>();
        builder.Services.AddSingleton<IAcumaticaDocumentClient>(_acumatica);
        builder.Services.AddScoped<DocumentProcessor>();
        builder.Services.AddScoped<IIntegrationCommandQueue, IntegrationCommandQueue>();
        builder.Services.AddScoped<DocumentReviewService>();
        builder.Services
            .AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization(Pvm.Api.Auth.ServiceCollectionExtensions.AddPvmPolicies);

        _app = builder.Build();
        _app.UseAuthentication();
        _app.Use(async (context, next) =>
        {
            // Stands in for AppUserClaimsMiddleware, which resolves the user from the database.
            if (resolveUser && context.User.Identity?.IsAuthenticated == true)
            {
                context.RequestServices.GetRequiredService<CurrentAppUserAccessor>().User = new CurrentAppUser(
                    Guid.NewGuid(),
                    null,
                    "operator@pvm.test",
                    null,
                    context.User.FindAll(AppAuthClaims.Role).Select(claim => claim.Value).ToArray());
            }

            await next(context);
        });
        _app.UseAuthorization();
        _app.MapDocumentIntakeEndpoints();
        _app.MapDocumentReviewEndpoints();
        await _app.StartAsync();
        return _app.GetTestClient();
    }

    private sealed class FakeAcumatica : IAcumaticaDocumentClient
    {
        public IReadOnlyList<InvoiceCandidate> SearchResults { get; set; } = [];

        public List<string> Queries { get; } = [];

        public List<string> Lookups { get; } = [];

        public Task<InvoiceCandidate?> GetInvoiceAsync(string referenceNumber, CancellationToken cancellationToken)
        {
            Lookups.Add(referenceNumber);
            return Task.FromResult(SearchResults.FirstOrDefault(invoice =>
                string.Equals(invoice.ReferenceNumber, referenceNumber, StringComparison.OrdinalIgnoreCase)));
        }

        public Task<IReadOnlyList<CandidateHit>> FindInvoicesAsync(PodExtraction extraction, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<InvoiceCandidate>> SearchInvoicesAsync(string query, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            return Task.FromResult(SearchResults);
        }

        public Task AttachFileAsync(string entity, string referenceNumber, string fileName, Stream pdf, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";
        public const string RoleHeader = "X-Test-Role";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers[RoleHeader].FirstOrDefault();
            if (role is null)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Email, "operator@pvm.test"), new Claim(AppAuthClaims.Role, role)],
                SchemeName,
                ClaimTypes.Name,
                AppAuthClaims.Role);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
