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
    public async Task Search_ByInvoice_FindsAReviewDocumentByTheInvoiceNumberReadFromThePaper()
    {
        var client = await StartAsync();
        var onReview = await SeedAsync(
            DocumentState.NeedsReview,
            reviewReason: ReviewReason.ClientNameConflict,
            extractionJson: """{"InvoiceNumbers":["INV190931"],"ClientName":"Dis-Chem Mondeor S007","candidates":[]}""");
        await SeedAsync(
            DocumentState.NeedsReview,
            reviewReason: ReviewReason.NoSecondSignal,
            extractionJson: """{"InvoiceNumbers":["INV190932"],"candidates":[]}""");

        var response = await client.SendAsync(Get("/api/documents?invoice=inv190931", AppRoles.Viewer));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(onReview, item.GetProperty("id").GetGuid());
        Assert.Equal("NeedsReview", item.GetProperty("state").GetString());
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

    [Theory]
    [InlineData(DocumentState.NeedsReview, 60, false, true)]
    [InlineData(DocumentState.Failed, 60, false, true)]
    [InlineData(DocumentState.Received, 31, false, true)]
    [InlineData(DocumentState.Processing, 31, false, true)]
    [InlineData(DocumentState.Received, 5, false, false)]
    [InlineData(DocumentState.Processing, 5, false, false)]
    [InlineData(DocumentState.Filed, 60, false, false)]
    [InlineData(DocumentState.Filed, 60, true, false)]
    public async Task Detail_CanDelete_FollowsTheDeleteRule(DocumentState state, int minutesOld, bool attached, bool expected)
    {
        var client = await StartAsync();
        var id = await SeedAsync(
            state,
            uploadedAt: DateTimeOffset.UtcNow.AddMinutes(-minutesOld),
            matchedReference: state == DocumentState.Filed ? "INV1" : null,
            acumaticaAttachedAt: attached ? DateTimeOffset.UtcNow.AddMinutes(-10) : null);

        var response = await client.SendAsync(Get($"/api/documents/{id:D}", AppRoles.Viewer));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expected, body.GetProperty("canDelete").GetBoolean());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Delete_BlankReason_Returns400(string reason)
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.NeedsReview);

        var response = await client.SendAsync(Delete(id, reason));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DocumentState.NeedsReview, (await LoadAsync(id)).State);
    }

    [Fact]
    public async Task Delete_NoBody_Returns400()
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.NeedsReview);

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/documents/{id:D}/delete")
        {
            Content = JsonContent.Create(new { }),
            Headers = { { TestAuthHandler.RoleHeader, AppRoles.Operator } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ReasonOver500_Returns400()
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.NeedsReview);

        var response = await client.SendAsync(Delete(id, new string('x', 501)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Viewer_Returns403()
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.NeedsReview);

        var response = await client.SendAsync(Delete(id, "Duplicate", AppRoles.Viewer));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(DocumentState.NeedsReview, (await LoadAsync(id)).State);
    }

    [Fact]
    public async Task Delete_Anonymous_Returns401()
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.NeedsReview);

        var response = await client.PostAsJsonAsync($"/api/documents/{id:D}/delete", new { reason = "Duplicate" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_UnknownDocument_Returns404()
    {
        var client = await StartAsync();

        var response = await client.SendAsync(Delete(Guid.NewGuid(), "Duplicate"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_UserNotResolved_Returns500_AndDeletesNothing()
    {
        var client = await StartAsync(resolveUser: false);
        var id = await SeedAsync(DocumentState.NeedsReview);

        var response = await client.SendAsync(Delete(id, "Duplicate"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(DocumentState.NeedsReview, (await LoadAsync(id)).State);
    }

    [Fact]
    public async Task Delete_Filed_Returns409_DocumentFiled()
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.Filed, matchedReference: "INV1");

        var response = await client.SendAsync(Delete(id, "Duplicate"));

        await AssertConflictAsync(response, "document-filed", "Send it back to review first.");
        Assert.Equal(DocumentState.Filed, (await LoadAsync(id)).State);
    }

    [Fact]
    public async Task Delete_AttachedInAcumatica_Returns409_DocumentAttached()
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.Filed, matchedReference: "INV1", acumaticaAttachedAt: DateTimeOffset.UtcNow.AddHours(-1));

        var response = await client.SendAsync(Delete(id, "Duplicate"));

        await AssertConflictAsync(response, "document-attached", "Acumatica");
        Assert.Equal(DocumentState.Filed, (await LoadAsync(id)).State);
    }

    [Theory]
    [InlineData(DocumentState.Received)]
    [InlineData(DocumentState.Processing)]
    public async Task Delete_RecentlyUploaded_Returns409_DocumentProcessing(DocumentState state)
    {
        var client = await StartAsync();
        var id = await SeedAsync(state, uploadedAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        var response = await client.SendAsync(Delete(id, "Duplicate"));

        await AssertConflictAsync(response, "document-processing", "Wait until processing ends.");
        Assert.Equal(state, (await LoadAsync(id)).State);
    }


    [Fact]
    public async Task GeneralList_NewestFirst_OnlyGeneral_WithTheGeneralFields()
    {
        var client = await StartAsync();
        var now = DateTimeOffset.UtcNow;
        var older = await SeedGeneralAsync(uploadedAt: now.AddDays(-2), title: "Fleet licence", note: "Keep for SARS");
        var newer = await SeedGeneralAsync(
            uploadedAt: now.AddHours(-1),
            autoTitle: "Eskom statement, October",
            summary: "Electricity account for October.",
            documentDate: new DateOnly(2026, 10, 31),
            searchWords: ["Eskom", "4410"],
            thumbnail: true);
        await SeedAsync(DocumentState.Filed, uploadedAt: now, matchedReference: "INV1");

        var response = await client.SendAsync(Get("/api/documents/general", AppRoles.Viewer));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([newer, older], items.Select(item => item.GetProperty("id").GetGuid()));
        Assert.Equal(2, body.GetProperty("total").GetInt32());
        Assert.Equal(50, body.GetProperty("pageSize").GetInt32());
        var first = items[0];
        Assert.Equal("Filed", first.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("title").ValueKind);
        Assert.Equal("Eskom statement, October", first.GetProperty("autoTitle").GetString());
        Assert.Equal("Electricity account for October.", first.GetProperty("summary").GetString());
        Assert.Equal("2026-10-31", first.GetProperty("documentDate").GetString());
        Assert.Equal(["Eskom", "4410"], first.GetProperty("searchWords").EnumerateArray().Select(word => word.GetString()));
        Assert.True(first.GetProperty("hasThumbnail").GetBoolean());
        Assert.Equal("Dispatch phone 1", first.GetProperty("scannerDeviceName").GetString());
        Assert.Equal("Thabo", first.GetProperty("scannerStaffName").GetString());
        Assert.Equal("Fleet licence", items[1].GetProperty("title").GetString());
        Assert.Equal("Keep for SARS", items[1].GetProperty("note").GetString());
        Assert.False(items[1].GetProperty("hasThumbnail").GetBoolean());
        Assert.False(first.TryGetProperty("searchText", out _));
        Assert.False(first.TryGetProperty("blobPath", out _));
    }

    [Theory]
    [InlineData("FLEET", "title")]
    [InlineData("eskom statement", "autoTitle")]
    [InlineData("electricity ACCOUNT", "summary")]
    [InlineData("4410", "searchWords")]
    [InlineData("sars", "note")]
    public async Task GeneralList_Q_MatchesEachField_IgnoringCase(string q, string field)
    {
        var client = await StartAsync();
        var match = field switch
        {
            "title" => await SeedGeneralAsync(title: "Fleet licence"),
            "autoTitle" => await SeedGeneralAsync(autoTitle: "Eskom statement, October"),
            "summary" => await SeedGeneralAsync(summary: "Electricity account for October."),
            "searchWords" => await SeedGeneralAsync(searchWords: ["Eskom", "4410"]),
            _ => await SeedGeneralAsync(note: "Keep for SARS"),
        };
        await SeedGeneralAsync(title: "Something else", note: "Nothing to see", searchWords: ["other"]);

        var response = await client.SendAsync(Get($"/api/documents/general?q={Uri.EscapeDataString(q)}", AppRoles.Viewer));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(match, item.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task GeneralList_FiltersByDeviceStaffAndDates()
    {
        var client = await StartAsync();
        var now = DateTimeOffset.UtcNow;
        var phone2 = await SeedDeviceAsync("Dispatch phone 2");
        var anna = await SeedStaffAsync("Anna");
        var match = await SeedGeneralAsync(uploadedAt: now.AddDays(-3), deviceId: phone2, staffId: anna, staffName: "Anna");
        await SeedGeneralAsync(uploadedAt: now.AddDays(-3));
        await SeedGeneralAsync(uploadedAt: now.AddDays(-3), deviceId: phone2);
        await SeedGeneralAsync(uploadedAt: now.AddDays(-10), deviceId: phone2, staffId: anna, staffName: "Anna");

        var from = Uri.EscapeDataString(now.AddDays(-5).ToString("O"));
        var to = Uri.EscapeDataString(now.AddDays(-1).ToString("O"));
        var response = await client.SendAsync(Get(
            $"/api/documents/general?device={phone2:D}&staff={anna:D}&from={from}&to={to}",
            AppRoles.Viewer));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(match, item.GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData("device=not-a-guid")]
    [InlineData("staff=not-a-guid")]
    public async Task GeneralList_BadDeviceOrStaff_Returns400(string query)
    {
        var client = await StartAsync();

        var response = await client.SendAsync(Get($"/api/documents/general?{query}", AppRoles.Viewer));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GeneralList_PagesOf50()
    {
        var client = await StartAsync();
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 51; index++)
        {
            await SeedGeneralAsync(uploadedAt: now.AddMinutes(-index));
        }

        var body = await (await client.SendAsync(Get("/api/documents/general?page=2", AppRoles.Viewer)))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(51, body.GetProperty("total").GetInt32());
        Assert.Equal(2, body.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task GeneralFilters_TheDevicesAndStaffOfGeneralDocuments_ByTheirCurrentNames()
    {
        var client = await StartAsync();
        var phone2 = await SeedDeviceAsync("Dispatch phone 2");
        var anna = await SeedStaffAsync("Anna");
        await SeedGeneralAsync(deviceId: phone2, staffId: anna, staffName: "Ana");
        await SeedGeneralAsync();
        await SeedGeneralAsync();

        var response = await client.SendAsync(Get("/api/documents/general/filters", AppRoles.Viewer));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            ["Dispatch phone 1", "Dispatch phone 2"],
            body.GetProperty("devices").EnumerateArray().Select(item => item.GetProperty("name").GetString()));
        Assert.Equal(
            ["Anna", "Thabo"],
            body.GetProperty("staff").EnumerateArray().Select(item => item.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task GeneralEndpoints_Anonymous_Return401()
    {
        var client = await StartAsync();
        var id = await SeedGeneralAsync(thumbnail: true);

        foreach (var url in new[] { "/api/documents/general", "/api/documents/general/filters", $"/api/documents/{id:D}/thumbnail" })
        {
            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Search_DefaultsToPod_AndAnExplicitTypeGeneralStillWorks()
    {
        var client = await StartAsync();
        var pod = await SeedAsync(DocumentState.Filed, matchedReference: "INV1");
        var general = await SeedGeneralAsync();

        var all = await (await client.SendAsync(Get("/api/documents", AppRoles.Viewer))).Content.ReadFromJsonAsync<JsonElement>();
        var generalOnly = await (await client.SendAsync(Get("/api/documents?type=general", AppRoles.Viewer))).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(pod, Assert.Single(all.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(general, Assert.Single(generalOnly.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task ReviewList_LeavesOutGeneralDocuments_EvenFailedOrStuck()
    {
        var client = await StartAsync();
        var now = DateTimeOffset.UtcNow;
        var pod = await SeedAsync(DocumentState.Failed, uploadedAt: now.AddHours(-2));
        await SeedGeneralAsync(state: DocumentState.Failed, uploadedAt: now.AddHours(-2));
        await SeedGeneralAsync(state: DocumentState.Processing, uploadedAt: now.AddHours(-2));

        var body = await (await client.SendAsync(Get("/api/documents/review", AppRoles.Viewer))).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(pod, Assert.Single(body.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Detail_General_ReturnsTheGeneralFields_AndCanDeleteWhenFiled()
    {
        var client = await StartAsync();
        var id = await SeedGeneralAsync(
            title: "Fleet licence",
            note: "Keep for SARS",
            autoTitle: "Vehicle licence disc",
            summary: "Licence disc for the delivery truck.",
            documentDate: new DateOnly(2026, 9, 1),
            searchWords: ["licence", "truck"],
            thumbnail: true);

        var body = await (await client.SendAsync(Get($"/api/documents/{id:D}", AppRoles.Viewer))).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("GENERAL", body.GetProperty("documentType").GetString());
        Assert.Equal("Fleet licence", body.GetProperty("title").GetString());
        Assert.Equal("Keep for SARS", body.GetProperty("note").GetString());
        Assert.Equal("Vehicle licence disc", body.GetProperty("autoTitle").GetString());
        Assert.Equal("Licence disc for the delivery truck.", body.GetProperty("summary").GetString());
        Assert.Equal("2026-09-01", body.GetProperty("documentDate").GetString());
        Assert.Equal(["licence", "truck"], body.GetProperty("searchWords").EnumerateArray().Select(word => word.GetString()));
        Assert.True(body.GetProperty("hasThumbnail").GetBoolean());
        Assert.True(body.GetProperty("canDelete").GetBoolean());
    }

    [Theory]
    [InlineData(DocumentState.Filed, 60, true)]
    [InlineData(DocumentState.Failed, 60, true)]
    [InlineData(DocumentState.Received, 31, true)]
    [InlineData(DocumentState.Processing, 31, true)]
    [InlineData(DocumentState.Received, 5, false)]
    [InlineData(DocumentState.Processing, 5, false)]
    public async Task Detail_General_CanDelete_FollowsTheGeneralDeleteRule(DocumentState state, int minutesOld, bool expected)
    {
        var client = await StartAsync();
        var id = await SeedGeneralAsync(state: state, uploadedAt: DateTimeOffset.UtcNow.AddMinutes(-minutesOld));

        var body = await (await client.SendAsync(Get($"/api/documents/{id:D}", AppRoles.Viewer))).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(expected, body.GetProperty("canDelete").GetBoolean());
    }

    [Fact]
    public async Task Detail_Pod_HasTheNewFieldsEmpty()
    {
        var client = await StartAsync();
        var id = await SeedAsync(DocumentState.NeedsReview);

        var body = await (await client.SendAsync(Get($"/api/documents/{id:D}", AppRoles.Viewer))).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(JsonValueKind.Null, body.GetProperty("title").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("note").ValueKind);
        Assert.Empty(body.GetProperty("searchWords").EnumerateArray());
        Assert.False(body.GetProperty("hasThumbnail").GetBoolean());
    }

    [Fact]
    public async Task Thumbnail_ServesTheJpeg_Privately()
    {
        var client = await StartAsync();
        var id = await SeedGeneralAsync(thumbnail: true);

        var response = await client.SendAsync(Get($"/api/documents/{id:D}/thumbnail", AppRoles.Viewer));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("private", response.Headers.CacheControl?.ToString());
        Assert.Equal(Thumbnail, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Thumbnail_NoneOrUnknownDocument_Returns404()
    {
        var client = await StartAsync();
        var id = await SeedGeneralAsync();

        var none = await client.SendAsync(Get($"/api/documents/{id:D}/thumbnail", AppRoles.Viewer));
        var unknown = await client.SendAsync(Get($"/api/documents/{Guid.NewGuid():D}/thumbnail", AppRoles.Viewer));

        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    private static readonly byte[] Thumbnail = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0xFF, 0xD9];

    private Guid? _phone1;
    private Guid? _thabo;

    private async Task<Guid> SeedDeviceAsync(string name)
    {
        var id = Guid.NewGuid();
        await using var scope = _app!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PvmDbContext>();
        db.ScannerDevices.Add(new ScannerDeviceEntity { Id = id, Name = name, CreatedBy = "admin@pvm.test", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedStaffAsync(string name)
    {
        var id = Guid.NewGuid();
        await using var scope = _app!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PvmDbContext>();
        db.ScannerStaff.Add(new ScannerStaffEntity { Id = id, Name = name, Active = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>A general document; by default Filed, from "Dispatch phone 1" and staff "Thabo".</summary>
    private async Task<Guid> SeedGeneralAsync(
        DocumentState state = DocumentState.Filed,
        DateTimeOffset? uploadedAt = null,
        string? title = null,
        string? note = null,
        string? autoTitle = null,
        string? summary = null,
        DateOnly? documentDate = null,
        string[]? searchWords = null,
        bool thumbnail = false,
        Guid? deviceId = null,
        Guid? staffId = null,
        string? staffName = null)
    {
        _phone1 ??= await SeedDeviceAsync("Dispatch phone 1");
        _thabo ??= await SeedStaffAsync("Thabo");
        var id = Guid.NewGuid();
        var at = uploadedAt ?? DateTimeOffset.UtcNow;
        var blobPath = $"documents/GENERAL/{at:yyyy}/{at:MM}/{id:D}.pdf";
        var thumbnailPath = thumbnail ? $"documents/GENERAL/{at:yyyy}/{at:MM}/{id:D}.thumb.jpg" : null;
        await using var scope = _app!.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        await store.SaveAsync(blobPath, new MemoryStream(Pdf), CancellationToken.None);
        if (thumbnailPath is not null)
        {
            await store.SaveAsync(thumbnailPath, new MemoryStream(Thumbnail), CancellationToken.None);
        }

        var db = scope.ServiceProvider.GetRequiredService<PvmDbContext>();
        db.CapturedDocuments.Add(new CapturedDocumentEntity
        {
            Id = id,
            ClientDocumentId = Guid.NewGuid(),
            DocumentType = "GENERAL",
            State = state,
            BlobPath = blobPath,
            ThumbnailBlobPath = thumbnailPath,
            PageCount = 2,
            SizeBytes = Pdf.Length,
            Sha256 = new string('a', 64),
            Title = title,
            Note = note,
            AutoTitle = autoTitle,
            Summary = summary,
            DocumentDate = documentDate,
            SearchWords = searchWords ?? [],
            SearchText = GeneralDocumentFields.SearchText(title, note, autoTitle, summary, searchWords),
            UploadedBy = "device:Dispatch phone 1",
            UploadedAt = at,
            ScannerDeviceId = deviceId ?? _phone1,
            ScannerStaffId = staffId ?? _thabo,
            ScannerStaffName = staffName ?? "Thabo",
            UpdatedAt = at,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task AssertConflictAsync(HttpResponseMessage response, string code, string detailContains)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Contains(detailContains, problem.GetProperty("detail").GetString());
    }

    private static HttpRequestMessage Delete(Guid id, string reason, string role = AppRoles.Operator)
        => new(HttpMethod.Post, $"/api/documents/{id:D}/delete")
        {
            Content = JsonContent.Create(new { reason }),
            Headers = { { TestAuthHandler.RoleHeader, role } }
        };

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
