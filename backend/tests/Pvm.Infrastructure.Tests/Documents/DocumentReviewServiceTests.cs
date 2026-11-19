using System.Data.Common;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pvm.Application.Documents;
using Pvm.Application.Messaging;
using Pvm.Infrastructure.Documents;
using Pvm.Infrastructure.Messaging;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Documents;

public sealed class DocumentReviewServiceTests : IAsyncLifetime
{
    private const string User = "operator@pvm.co.za";

    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7 fake scan");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();
    private readonly FakeAcumatica _acumatica = new();
    private readonly FakeStore _store = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await DatabaseMigrationRunner.MigrateAsync(db);
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Theory]
    [InlineData(DocumentState.NeedsReview)]
    [InlineData(DocumentState.Failed)]
    public async Task FileTo_OpenDocument_ShadowMode_FiledByUser_AttachPending_NoAcumaticaAttach(DocumentState state)
    {
        var id = await SeedAsync(state, reviewNote: "earlier note");
        _acumatica.SearchResults = [Invoice("INV001234", "Acme Stores (Pty) Ltd"), Invoice("INV0012345", "Other")];

        var result = await FileToAsync(id, "inv001234", AcumaticaAttachMode.Shadow);

        Assert.Equal(FileToOutcome.Filed, result.Outcome);
        Assert.Equal("INV001234", result.Invoice!.ReferenceNumber);
        var row = await LoadAsync(id);
        Assert.Equal(DocumentState.Filed, row.State);
        Assert.Equal("INV001234", row.MatchedReference);
        Assert.Equal("Acme Stores (Pty) Ltd", row.MatchedCustomerName);
        Assert.Equal($"user:{User}", row.MatchedBy);
        Assert.Equal(User, row.ResolvedBy);
        Assert.NotNull(row.ResolvedAt);
        Assert.True(row.AcumaticaAttachPending);
        Assert.Null(row.AcumaticaAttachLastAttemptAt);
        Assert.Null(row.AcumaticaAttachedAt);
        Assert.Null(row.ReviewNote);
        Assert.Equal(["inv001234"], _acumatica.Lookups);
        Assert.Empty(_acumatica.Queries);
        Assert.Empty(_acumatica.AttachAttempts);
    }

    [Fact]
    public async Task FileTo_WriteMode_AttachesWithTheFileName()
    {
        var id = await SeedAsync(DocumentState.NeedsReview);
        _acumatica.SearchResults = [Invoice("INV001234", "Acme Stores (Pty) Ltd")];

        var result = await FileToAsync(id, "INV001234", AcumaticaAttachMode.Write);

        Assert.Equal(FileToOutcome.Filed, result.Outcome);
        var attach = Assert.Single(_acumatica.Attached);
        Assert.Equal("SalesInvoice", attach.Entity);
        Assert.Equal("INV001234", attach.ReferenceNumber);
        Assert.Equal($"POD-INV001234-{id:D}.pdf", attach.FileName);
        Assert.Equal(PdfBytes, attach.Bytes);
        var row = await LoadAsync(id);
        Assert.Equal(DocumentState.Filed, row.State);
        Assert.False(row.AcumaticaAttachPending);
        Assert.NotNull(row.AcumaticaAttachedAt);
        Assert.NotNull(row.AcumaticaAttachLastAttemptAt);
    }

    [Fact]
    public async Task FileTo_WriteMode_AttachFails_StaysFiledWithAttachPending()
    {
        var id = await SeedAsync(DocumentState.NeedsReview);
        _acumatica.SearchResults = [Invoice("INV001234", "Acme Stores (Pty) Ltd")];
        _acumatica.AttachFails = true;

        var result = await FileToAsync(id, "INV001234", AcumaticaAttachMode.Write);

        Assert.Equal(FileToOutcome.Filed, result.Outcome);
        var row = await LoadAsync(id);
        Assert.Equal(DocumentState.Filed, row.State);
        Assert.True(row.AcumaticaAttachPending);
        Assert.Null(row.AcumaticaAttachedAt);
        Assert.NotNull(row.AcumaticaAttachLastAttemptAt);
    }

    [Theory]
    [InlineData(DocumentState.Filed)]
    [InlineData(DocumentState.Received)]
    [InlineData(DocumentState.Processing)]
    public async Task FileTo_NotOpen_Conflict_NoSearch_NoWrite(DocumentState state)
    {
        var id = await SeedAsync(state, matchedReference: state == DocumentState.Filed ? "INV1" : null);
        _acumatica.SearchResults = [Invoice("INV001234", "Acme Stores (Pty) Ltd")];

        var result = await FileToAsync(id, "INV001234", AcumaticaAttachMode.Write);

        Assert.Equal(FileToOutcome.NotOpen, result.Outcome);
        Assert.Empty(_acumatica.Lookups);
        var row = await LoadAsync(id);
        Assert.Equal(state, row.State);
        Assert.Null(row.ResolvedBy);
    }

    [Fact]
    public async Task FileTo_RowLeavesReviewDuringTheSearch_Conflict_NoWrite()
    {
        var id = await SeedAsync(DocumentState.NeedsReview);
        _acumatica.SearchResults = [Invoice("INV001234", "Acme Stores (Pty) Ltd")];
        _acumatica.OnSearch = async () =>
        {
            // Another reviewer files it to a different invoice while this request searches.
            await using var other = CreateDbContext();
            await other.CapturedDocuments.Where(item => item.Id == id).ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.State, DocumentState.Filed)
                .SetProperty(item => item.MatchedReference, "INV777"));
        };

        var result = await FileToAsync(id, "INV001234", AcumaticaAttachMode.Write);

        Assert.Equal(FileToOutcome.NotOpen, result.Outcome);
        Assert.Equal("INV777", (await LoadAsync(id)).MatchedReference);
        Assert.Empty(_acumatica.AttachAttempts);
    }

    [Fact]
    public async Task FileTo_NoExactReferenceInTheResults_InvoiceNotFound_NoWrite()
    {
        var id = await SeedAsync(DocumentState.NeedsReview);
        _acumatica.SearchResults = [Invoice("INV0012345", "Acme Stores (Pty) Ltd")];

        var result = await FileToAsync(id, "INV001234", AcumaticaAttachMode.Write);

        Assert.Equal(FileToOutcome.InvoiceNotFound, result.Outcome);
        Assert.Equal(DocumentState.NeedsReview, (await LoadAsync(id)).State);
    }

    [Fact]
    public async Task FileTo_UnknownDocument_NotFound()
    {
        var result = await FileToAsync(Guid.NewGuid(), "INV001234", AcumaticaAttachMode.Shadow);

        Assert.Equal(FileToOutcome.NotFound, result.Outcome);
    }

    [Theory]
    [InlineData(DocumentState.NeedsReview)]
    [InlineData(DocumentState.Failed)]
    public async Task Reject_OpenDocument_KeepsState_SetsNoteAndRejectedMarker(DocumentState state)
    {
        var id = await SeedAsync(state);

        RejectOutcome outcome;
        await using (var db = CreateDbContext())
        {
            outcome = await CreateService(db, AcumaticaAttachMode.Shadow).RejectAsync(id, "bad scan", User, default);
        }

        Assert.Equal(RejectOutcome.Rejected, outcome);
        var row = await LoadAsync(id);
        Assert.Equal(state, row.State);
        Assert.Equal("bad scan", row.ReviewNote);
        Assert.Equal(User, row.RejectedBy);
        Assert.NotNull(row.RejectedAt);
        Assert.Equal(User, row.ResolvedBy);
        Assert.NotNull(row.ResolvedAt);
    }

    [Theory]
    [InlineData(DocumentState.Filed)]
    [InlineData(DocumentState.Processing)]
    public async Task Reject_NotOpen_Conflict(DocumentState state)
    {
        var id = await SeedAsync(state);

        await using var db = CreateDbContext();
        var outcome = await CreateService(db, AcumaticaAttachMode.Shadow).RejectAsync(id, "bad scan", User, default);

        Assert.Equal(RejectOutcome.NotOpen, outcome);
        Assert.Null((await LoadAsync(id)).RejectedAt);
    }

    [Fact]
    public async Task Reject_AlreadyRejected_Conflict_KeepsTheFirstNote()
    {
        var id = await SeedAsync(DocumentState.NeedsReview);
        await using (var db = CreateDbContext())
        {
            await CreateService(db, AcumaticaAttachMode.Shadow).RejectAsync(id, "bad scan", User, default);
        }

        await using (var db = CreateDbContext())
        {
            var outcome = await CreateService(db, AcumaticaAttachMode.Shadow).RejectAsync(id, "second", "other@pvm.co.za", default);
            Assert.Equal(RejectOutcome.NotOpen, outcome);
        }

        Assert.Equal("bad scan", (await LoadAsync(id)).ReviewNote);
    }

    [Fact]
    public async Task Reject_UnknownDocument_NotFound()
    {
        await using var db = CreateDbContext();
        var outcome = await CreateService(db, AcumaticaAttachMode.Shadow).RejectAsync(Guid.NewGuid(), "bad scan", User, default);

        Assert.Equal(RejectOutcome.NotFound, outcome);
    }

    [Fact]
    public async Task FileTo_RejectedDocument_FilesAndClearsTheRejection()
    {
        var id = await SeedAsync(DocumentState.NeedsReview);
        await using (var db = CreateDbContext())
        {
            await CreateService(db, AcumaticaAttachMode.Shadow).RejectAsync(id, "bad scan", User, default);
        }

        _acumatica.SearchResults = [Invoice("INV001234", "Acme Stores (Pty) Ltd")];
        var result = await FileToAsync(id, "INV001234", AcumaticaAttachMode.Shadow);

        Assert.Equal(FileToOutcome.Filed, result.Outcome);
        var row = await LoadAsync(id);
        Assert.Equal(DocumentState.Filed, row.State);
        Assert.Null(row.RejectedAt);
        Assert.Null(row.RejectedBy);
        Assert.Null(row.ReviewNote);
    }

    [Fact]
    public async Task ReturnToReview_FiledNotAttached_BackOnTheReviewList_MatchCleared()
    {
        var id = await SeedAsync(DocumentState.NeedsReview);
        _acumatica.SearchResults = [Invoice("INV001234", "Acme Stores (Pty) Ltd")];
        await FileToAsync(id, "INV001234", AcumaticaAttachMode.Shadow);

        var outcome = await ReturnToReviewAsync(id, "wrong invoice; split shipment", "lead@pvm.co.za");

        Assert.Equal(ReturnToReviewOutcome.Returned, outcome);
        var row = await LoadAsync(id);
        Assert.Equal(DocumentState.NeedsReview, row.State);
        Assert.Null(row.ReviewReason);
        Assert.Null(row.MatchedReference);
        Assert.Null(row.MatchedCustomerName);
        Assert.Null(row.MatchedBy);
        Assert.False(row.AcumaticaAttachPending);
        Assert.Null(row.AcumaticaAttachedAt);
        Assert.Null(row.RejectedAt);
        Assert.Equal("wrong invoice; split shipment", row.ReviewNote);
        Assert.Equal("lead@pvm.co.za", row.ResolvedBy);
        Assert.NotNull(row.ResolvedAt);
    }

    [Fact]
    public async Task ReturnToReview_AlreadyAttached_NoWrite()
    {
        var id = await SeedAsync(DocumentState.NeedsReview);
        _acumatica.SearchResults = [Invoice("INV001234", "Acme Stores (Pty) Ltd")];
        await FileToAsync(id, "INV001234", AcumaticaAttachMode.Write);
        Assert.NotNull((await LoadAsync(id)).AcumaticaAttachedAt);

        var outcome = await ReturnToReviewAsync(id, "wrong invoice", User);

        Assert.Equal(ReturnToReviewOutcome.AlreadyAttached, outcome);
        var row = await LoadAsync(id);
        Assert.Equal(DocumentState.Filed, row.State);
        Assert.Equal("INV001234", row.MatchedReference);
        Assert.Null(row.ReviewNote);
    }

    [Theory]
    [InlineData(DocumentState.NeedsReview)]
    [InlineData(DocumentState.Failed)]
    [InlineData(DocumentState.Received)]
    public async Task ReturnToReview_NotFiled_NoWrite(DocumentState state)
    {
        var id = await SeedAsync(state);

        var outcome = await ReturnToReviewAsync(id, "wrong invoice", User);

        Assert.Equal(ReturnToReviewOutcome.NotFiled, outcome);
        var row = await LoadAsync(id);
        Assert.Equal(state, row.State);
        Assert.Null(row.ReviewNote);
    }

    [Fact]
    public async Task ReturnToReview_UnknownDocument_NotFound()
    {
        var outcome = await ReturnToReviewAsync(Guid.NewGuid(), "wrong invoice", User);

        Assert.Equal(ReturnToReviewOutcome.NotFound, outcome);
    }

    [Fact]
    public async Task Backfill_NeverAttachesADocumentReturnedToReview()
    {
        var id = await SeedAsync(DocumentState.NeedsReview);
        _acumatica.SearchResults = [Invoice("INV001234", "Acme Stores (Pty) Ltd")];
        await FileToAsync(id, "INV001234", AcumaticaAttachMode.Shadow);
        Assert.Equal(ReturnToReviewOutcome.Returned, await ReturnToReviewAsync(id, "wrong invoice", User));

        int attached;
        await using (var db = CreateDbContext())
        {
            attached = await CreateProcessor(db, AcumaticaAttachMode.Write).BackfillPendingAttachmentsAsync(default);
        }

        Assert.Equal(0, attached);
        Assert.Empty(_acumatica.AttachAttempts);
    }

    [Theory]
    [InlineData(DocumentState.NeedsReview)]
    [InlineData(DocumentState.Failed)]
    [InlineData(DocumentState.Received)]
    [InlineData(DocumentState.Processing)]
    public async Task Backfill_NeverAttachesARowThatIsNotFiled_EvenWithAStaleMatch(DocumentState state)
    {
        var id = await SeedAsync(state, matchedReference: "INV001234");
        await using (var db = CreateDbContext())
        {
            await db.CapturedDocuments.Where(item => item.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.AcumaticaAttachPending, true));
        }

        int attached;
        await using (var db = CreateDbContext())
        {
            attached = await CreateProcessor(db, AcumaticaAttachMode.Write).BackfillPendingAttachmentsAsync(default);
        }

        Assert.Equal(0, attached);
        Assert.Empty(_acumatica.AttachAttempts);
    }

    [Theory]
    [InlineData(DocumentState.Failed, 0)]
    [InlineData(DocumentState.Received, 31)]
    [InlineData(DocumentState.Processing, 31)]
    public async Task ProcessAgain_FailedOrStuck_BackToReceived_NewMessageInTheOutbox(DocumentState state, int minutesOld)
    {
        var id = await SeedAsync(state, uploadedAt: DateTimeOffset.UtcNow.AddMinutes(-minutesOld), extractionJson: """{"invoiceNumbers":["INV1"]}""");
        await using (var db = CreateDbContext())
        {
            await db.CapturedDocuments.Where(item => item.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ReviewReason, ReviewReason.ReadFailure));
        }

        var outcome = await ProcessAgainAsync(id);

        Assert.Equal(ProcessAgainOutcome.Queued, outcome);
        var row = await LoadAsync(id);
        Assert.Equal(DocumentState.Received, row.State);
        Assert.Null(row.ReviewReason);
        Assert.Contains("INV1", row.ExtractionJson);
        await using var check = CreateDbContext();
        var message = Assert.Single(await check.IntegrationOutboxMessages.AsNoTracking().ToListAsync());
        Assert.Equal(IntegrationQueues.DocumentProcessing, message.QueueName);
        Assert.Equal(IntegrationMessageTypes.DocumentReceivedV1, message.MessageType);
        Assert.Equal(id.ToString("D"), message.CorrelationId);
        Assert.NotEqual(id, message.Id);
        Assert.Contains(id.ToString("D"), message.PayloadJson);
    }

    [Fact]
    public async Task ProcessAgain_Twice_TwoMessagesWithDifferentIds()
    {
        var id = await SeedAsync(DocumentState.Failed);
        Assert.Equal(ProcessAgainOutcome.Queued, await ProcessAgainAsync(id));
        await using (var db = CreateDbContext())
        {
            await db.CapturedDocuments.Where(item => item.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.State, DocumentState.Failed));
        }

        Assert.Equal(ProcessAgainOutcome.Queued, await ProcessAgainAsync(id));

        await using var check = CreateDbContext();
        var ids = await check.IntegrationOutboxMessages.AsNoTracking().Select(message => message.Id).ToListAsync();
        Assert.Equal(2, ids.Distinct().Count());
    }

    [Theory]
    [InlineData(DocumentState.Filed, 120)]
    [InlineData(DocumentState.NeedsReview, 120)]
    [InlineData(DocumentState.Received, 5)]
    [InlineData(DocumentState.Processing, 29)]
    public async Task ProcessAgain_NotFailedOrStuck_NoWrite_NoMessage(DocumentState state, int minutesOld)
    {
        var id = await SeedAsync(state, uploadedAt: DateTimeOffset.UtcNow.AddMinutes(-minutesOld), matchedReference: state == DocumentState.Filed ? "INV1" : null);

        var outcome = await ProcessAgainAsync(id);

        Assert.Equal(ProcessAgainOutcome.NotRetryable, outcome);
        Assert.Equal(state, (await LoadAsync(id)).State);
        await using var check = CreateDbContext();
        Assert.Empty(await check.IntegrationOutboxMessages.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ProcessAgain_UnknownDocument_NotFound()
    {
        Assert.Equal(ProcessAgainOutcome.NotFound, await ProcessAgainAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ProcessAgain_RejectedFailedDocument_ClearsTheRejectionSoItShowsAgain()
    {
        var id = await SeedAsync(DocumentState.Failed);
        await using (var db = CreateDbContext())
        {
            await CreateService(db, AcumaticaAttachMode.Shadow).RejectAsync(id, "gave up", User, default);
        }

        Assert.Equal(ProcessAgainOutcome.Queued, await ProcessAgainAsync(id));

        var row = await LoadAsync(id);
        Assert.Equal(DocumentState.Received, row.State);
        Assert.Null(row.RejectedAt);
        Assert.Null(row.RejectedBy);
    }

    [Fact]
    public async Task Delete_FiledGeneral_Deleted_PdfAndThumbnailGone_AuditHasTheTitle()
    {
        var id = await SeedAsync(DocumentState.Filed, documentType: "GENERAL", title: "Fleet licence", thumbnail: true);
        var before = await LoadAsync(id);

        var result = await DeleteAsync(id, "Old copy");

        Assert.Equal(DeleteOutcome.Deleted, result.Outcome);
        Assert.Null(await FindAsync(id));
        Assert.Equal([before.BlobPath, before.ThumbnailBlobPath!], _store.Deleted);
        await using var check = CreateDbContext();
        var audit = Assert.Single(await check.AuditEvents.AsNoTracking().Where(audit => audit.EntityType == "CapturedDocument").ToListAsync());
        using var details = JsonDocument.Parse(audit.DetailsJson!);
        Assert.Equal("GENERAL", details.RootElement.GetProperty("documentType").GetString());
        Assert.Equal("Filed", details.RootElement.GetProperty("state").GetString());
        Assert.Equal("Fleet licence", details.RootElement.GetProperty("title").GetString());
        Assert.Equal(before.ThumbnailBlobPath, details.RootElement.GetProperty("thumbnailBlobPath").GetString());
    }

    [Theory]
    [InlineData(DocumentState.Failed, 60)]
    [InlineData(DocumentState.Received, 31)]
    [InlineData(DocumentState.Processing, 31)]
    public async Task Delete_General_FailedOrStuck_Deleted(DocumentState state, int minutesOld)
    {
        var id = await SeedAsync(state, documentType: "GENERAL", uploadedAt: DateTimeOffset.UtcNow.AddMinutes(-minutesOld));

        var result = await DeleteAsync(id, "Test scan");

        Assert.Equal(DeleteOutcome.Deleted, result.Outcome);
        Assert.Null(await FindAsync(id));
    }

    [Fact]
    public async Task Delete_General_RecentlyUploaded_Processing_NothingChanged()
    {
        var id = await SeedAsync(DocumentState.Processing, documentType: "GENERAL", uploadedAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        var result = await DeleteAsync(id, "Too early");

        Assert.Equal(DeleteOutcome.Processing, result.Outcome);
        await AssertUntouchedAsync(id, DocumentState.Processing);
    }

    [Fact]
    public async Task Delete_FiledPod_StillFiled_NothingChanged()
    {
        var id = await SeedAsync(DocumentState.Filed, matchedReference: "INV1", thumbnail: true);

        var result = await DeleteAsync(id, "Wrong invoice");

        Assert.Equal(DeleteOutcome.Filed, result.Outcome);
        await AssertUntouchedAsync(id, DocumentState.Filed);
    }

    [Fact]
    public async Task ReviewActions_RefuseAGeneralDocument()
    {
        var failed = await SeedAsync(DocumentState.Failed, documentType: "GENERAL");
        var filed = await SeedAsync(DocumentState.Filed, documentType: "GENERAL");
        _acumatica.SearchResults = [Invoice("INV001234", "Acme Stores (Pty) Ltd")];

        Assert.Equal(FileToOutcome.NotOpen, (await FileToAsync(failed, "INV001234", AcumaticaAttachMode.Write)).Outcome);
        await using (var db = CreateDbContext())
        {
            Assert.Equal(RejectOutcome.NotOpen, await CreateService(db, AcumaticaAttachMode.Shadow).RejectAsync(failed, "Bad", User, default));
        }

        Assert.Equal(ReturnToReviewOutcome.NotFiled, await ReturnToReviewAsync(filed, "Wrong", User));
        Assert.Equal(DocumentState.Failed, (await LoadAsync(failed)).State);
        Assert.Null((await LoadAsync(failed)).RejectedAt);
        Assert.Equal(DocumentState.Filed, (await LoadAsync(filed)).State);
        Assert.Empty(_acumatica.AttachAttempts);
    }

    [Fact]
    public async Task Delete_NeedsReview_RowAndBlobGone_AuditEventWithTheFacts()
    {
        var deviceId = await SeedDeviceAsync("Dispatch phone 1");
        var id = await SeedAsync(DocumentState.NeedsReview, scannerDeviceId: deviceId, scannerStaffName: "Thabo");
        var before = await LoadAsync(id);

        var result = await DeleteAsync(id, "Duplicate scan of the same delivery note");

        Assert.Equal(DeleteOutcome.Deleted, result.Outcome);
        Assert.False(result.BlobDeleteFailed);
        Assert.Null(await FindAsync(id));
        Assert.False(_store.Blobs.ContainsKey(before.BlobPath));
        Assert.Equal([before.BlobPath], _store.Deleted);

        await using var check = CreateDbContext();
        var audit = Assert.Single(await check.AuditEvents.AsNoTracking().Where(audit => audit.EntityType == "CapturedDocument").ToListAsync());
        Assert.Equal("CapturedDocument", audit.EntityType);
        Assert.Equal(id.ToString("D"), audit.EntityId);
        Assert.Equal("document-deleted", audit.Action);
        Assert.Equal(User, audit.Actor);
        Assert.True(audit.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
        using var details = JsonDocument.Parse(audit.DetailsJson!);
        var root = details.RootElement;
        Assert.Equal(id, root.GetProperty("documentId").GetGuid());
        Assert.Equal("Duplicate scan of the same delivery note", root.GetProperty("reason").GetString());
        Assert.Equal(User, root.GetProperty("deletedBy").GetString());
        Assert.True(root.GetProperty("deletedAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Equal("POD", root.GetProperty("documentType").GetString());
        Assert.Equal("NeedsReview", root.GetProperty("state").GetString());
        Assert.Equal("driver@pvm.co.za", root.GetProperty("uploadedBy").GetString());
        Assert.Equal(deviceId, root.GetProperty("scannerDeviceId").GetGuid());
        Assert.Equal("Dispatch phone 1", root.GetProperty("scannerDeviceName").GetString());
        Assert.Equal("Thabo", root.GetProperty("scannerStaffName").GetString());
        Assert.Equal(before.ClientDocumentId, root.GetProperty("clientDocumentId").GetGuid());
        Assert.Equal(before.Sha256, root.GetProperty("sha256").GetString());
        Assert.Equal(before.BlobPath, root.GetProperty("blobPath").GetString());
    }

    [Fact]
    public async Task Delete_Failed_Deleted()
    {
        var id = await SeedAsync(DocumentState.Failed);

        var result = await DeleteAsync(id, "Not a delivery note");

        Assert.Equal(DeleteOutcome.Deleted, result.Outcome);
        Assert.Null(await FindAsync(id));
    }

    [Theory]
    [InlineData(DocumentState.Received)]
    [InlineData(DocumentState.Processing)]
    public async Task Delete_Stuck_Deleted(DocumentState state)
    {
        var id = await SeedAsync(state, uploadedAt: DateTimeOffset.UtcNow.AddMinutes(-31));

        var result = await DeleteAsync(id, "Stuck test upload");

        Assert.Equal(DeleteOutcome.Deleted, result.Outcome);
        Assert.Null(await FindAsync(id));
    }

    [Theory]
    [InlineData(DocumentState.Received)]
    [InlineData(DocumentState.Processing)]
    public async Task Delete_RecentlyUploadedAndProcessing_Processing_NothingChanged(DocumentState state)
    {
        var id = await SeedAsync(state, uploadedAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        var result = await DeleteAsync(id, "Too early");

        Assert.Equal(DeleteOutcome.Processing, result.Outcome);
        await AssertUntouchedAsync(id, state);
    }

    [Fact]
    public async Task Delete_Filed_Filed_NothingChanged()
    {
        var id = await SeedAsync(DocumentState.Filed, matchedReference: "INV1");

        var result = await DeleteAsync(id, "Wrong invoice");

        Assert.Equal(DeleteOutcome.Filed, result.Outcome);
        await AssertUntouchedAsync(id, DocumentState.Filed);
    }

    [Fact]
    public async Task Delete_AttachedInAcumatica_Attached_NothingChanged()
    {
        var id = await SeedAsync(DocumentState.Filed, matchedReference: "INV1", acumaticaAttachedAt: DateTimeOffset.UtcNow.AddHours(-1));

        var result = await DeleteAsync(id, "Wrong invoice");

        Assert.Equal(DeleteOutcome.Attached, result.Outcome);
        await AssertUntouchedAsync(id, DocumentState.Filed);
    }

    [Fact]
    public async Task Delete_Unknown_NotFound()
    {
        var result = await DeleteAsync(Guid.NewGuid(), "Gone");

        Assert.Equal(DeleteOutcome.NotFound, result.Outcome);
        Assert.Empty(_store.Deleted);
    }

    [Fact]
    public async Task Delete_RowFiledBetweenTheReadAndTheDelete_Filed_RowIntact()
    {
        var id = await SeedAsync(DocumentState.NeedsReview);
        var race = new BeforeDeleteInterceptor(async () =>
        {
            // The worker or another person files the document while this request runs.
            await using var other = CreateDbContext();
            await other.CapturedDocuments.Where(item => item.Id == id).ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.State, DocumentState.Filed)
                .SetProperty(item => item.MatchedReference, "INV777"));
        });

        DeleteResult result;
        await using (var db = CreateDbContext(race))
        {
            result = await CreateService(db, AcumaticaAttachMode.Shadow).DeleteAsync(id, "Duplicate", User, default);
        }

        Assert.True(race.Fired);
        Assert.Equal(DeleteOutcome.Filed, result.Outcome);
        var row = await LoadAsync(id);
        Assert.Equal(DocumentState.Filed, row.State);
        Assert.Equal("INV777", row.MatchedReference);
        Assert.True(_store.Blobs.ContainsKey(row.BlobPath));
        Assert.Empty(_store.Deleted);
        await using var check = CreateDbContext();
        Assert.Empty(await check.AuditEvents.AsNoTracking().Where(audit => audit.EntityType == "CapturedDocument").ToListAsync());
    }

    [Fact]
    public async Task Delete_BlobDeleteFails_DeletedWithWarning_RowGone_AuditKept()
    {
        var id = await SeedAsync(DocumentState.NeedsReview);
        _store.DeleteFails = true;

        var result = await DeleteAsync(id, "Duplicate");

        Assert.Equal(DeleteOutcome.Deleted, result.Outcome);
        Assert.True(result.BlobDeleteFailed);
        Assert.Null(await FindAsync(id));
        await using var check = CreateDbContext();
        Assert.Single(await check.AuditEvents.AsNoTracking().Where(audit => audit.EntityType == "CapturedDocument").ToListAsync());
    }

    [Fact]
    public async Task Delete_OnlyTheThumbnailFails_SaysTheThumbnail_NotThePdf()
    {
        var id = await SeedAsync(DocumentState.Filed, documentType: "GENERAL", thumbnail: true);
        _store.DeleteFailsFor = path => path.EndsWith(".thumb.jpg", StringComparison.Ordinal);
        var before = await LoadAsync(id);

        var result = await DeleteAsync(id, "Scanned twice");

        Assert.Equal(DeleteOutcome.Deleted, result.Outcome);
        Assert.True(result.BlobDeleteFailed);
        Assert.False(result.PdfDeleteFailed);
        Assert.True(result.ThumbnailDeleteFailed);
        Assert.Equal([before.BlobPath], _store.Deleted);
        Assert.Null(await FindAsync(id));
    }

    [Fact]
    public async Task Delete_OnlyThePdfFails_SaysThePdf()
    {
        var id = await SeedAsync(DocumentState.Filed, documentType: "GENERAL", thumbnail: true);
        _store.DeleteFailsFor = path => path.EndsWith(".pdf", StringComparison.Ordinal);
        var before = await LoadAsync(id);

        var result = await DeleteAsync(id, "Scanned twice");

        Assert.Equal(DeleteOutcome.Deleted, result.Outcome);
        Assert.True(result.PdfDeleteFailed);
        Assert.False(result.ThumbnailDeleteFailed);
        Assert.Equal([before.ThumbnailBlobPath!], _store.Deleted);
    }

    [Fact]
    public async Task ChangeInvoice_FiledInShadowMode_LinksTheNewInvoice_KeepsTheAttachPending_WritesTheAudit()
    {
        var id = await SeedAsync(DocumentState.Filed, matchedReference: "INV001234", matchedCustomerName: "Acme Stores (Pty) Ltd", attachPending: true);
        _acumatica.SearchResults = [Invoice("INV001240", "Midwater Spar")];

        var result = await ChangeInvoiceAsync(id, "inv001240", "Split delivery: this page is the second invoice");

        Assert.Equal(ChangeInvoiceOutcome.Changed, result.Outcome);
        Assert.Equal("INV001240", result.Invoice!.ReferenceNumber);
        Assert.Equal("INV001234", result.PreviousReference);
        var row = await LoadAsync(id);
        Assert.Equal(DocumentState.Filed, row.State);
        Assert.Equal("INV001240", row.MatchedReference);
        Assert.Equal("Midwater Spar", row.MatchedCustomerName);
        Assert.Equal($"user:{User}", row.MatchedBy);
        Assert.Equal(User, row.ResolvedBy);
        Assert.NotNull(row.ResolvedAt);
        Assert.True(row.AcumaticaAttachPending);
        Assert.Null(row.AcumaticaAttachedAt);
        Assert.Equal(["inv001240"], _acumatica.Lookups);
        Assert.Empty(_acumatica.AttachAttempts);

        var audit = Assert.Single(await DocumentAuditsAsync());
        Assert.Equal(id.ToString("D"), audit.EntityId);
        Assert.Equal("document-invoice-changed", audit.Action);
        Assert.Equal(User, audit.Actor);
        using var details = JsonDocument.Parse(audit.DetailsJson!);
        var root = details.RootElement;
        Assert.Equal(id, root.GetProperty("documentId").GetGuid());
        Assert.Equal("INV001234", root.GetProperty("oldInvoice").GetString());
        Assert.Equal("Acme Stores (Pty) Ltd", root.GetProperty("oldCustomerName").GetString());
        Assert.Equal("INV001240", root.GetProperty("newInvoice").GetString());
        Assert.Equal("Midwater Spar", root.GetProperty("newCustomerName").GetString());
        Assert.Equal("Split delivery: this page is the second invoice", root.GetProperty("reason").GetString());
        Assert.Equal(User, root.GetProperty("changedBy").GetString());
        Assert.True(root.GetProperty("changedAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task ChangeInvoice_WithoutAReason_AuditHasANullReason()
    {
        var id = await SeedAsync(DocumentState.Filed, matchedReference: "INV001234", matchedCustomerName: "Acme Stores (Pty) Ltd");
        _acumatica.SearchResults = [Invoice("INV001240", "Midwater Spar")];

        var result = await ChangeInvoiceAsync(id, "INV001240");

        Assert.Equal(ChangeInvoiceOutcome.Changed, result.Outcome);
        var audit = Assert.Single(await DocumentAuditsAsync());
        using var details = JsonDocument.Parse(audit.DetailsJson!);
        Assert.Equal(JsonValueKind.Null, details.RootElement.GetProperty("reason").ValueKind);
    }

    [Theory]
    [InlineData(DocumentState.NeedsReview)]
    [InlineData(DocumentState.Failed)]
    [InlineData(DocumentState.Received)]
    [InlineData(DocumentState.Processing)]
    public async Task ChangeInvoice_NotFiled_NoLookup_NoWrite(DocumentState state)
    {
        var id = await SeedAsync(state);
        _acumatica.SearchResults = [Invoice("INV001240", "Midwater Spar")];

        var result = await ChangeInvoiceAsync(id, "INV001240");

        Assert.Equal(ChangeInvoiceOutcome.NotFiled, result.Outcome);
        Assert.Empty(_acumatica.Lookups);
        Assert.Null((await LoadAsync(id)).MatchedReference);
        Assert.Empty(await DocumentAuditsAsync());
    }

    [Fact]
    public async Task ChangeInvoice_AttachedInAcumatica_NoLookup_NoWrite()
    {
        var id = await SeedAsync(DocumentState.Filed, matchedReference: "INV001234", acumaticaAttachedAt: DateTimeOffset.UtcNow.AddHours(-1));
        _acumatica.SearchResults = [Invoice("INV001240", "Midwater Spar")];

        var result = await ChangeInvoiceAsync(id, "INV001240");

        Assert.Equal(ChangeInvoiceOutcome.Attached, result.Outcome);
        Assert.Empty(_acumatica.Lookups);
        Assert.Equal("INV001234", (await LoadAsync(id)).MatchedReference);
        Assert.Empty(await DocumentAuditsAsync());
    }

    [Fact]
    public async Task ChangeInvoice_GeneralDocument_NotAPod()
    {
        var id = await SeedAsync(DocumentState.Filed, documentType: "GENERAL");
        _acumatica.SearchResults = [Invoice("INV001240", "Midwater Spar")];

        var result = await ChangeInvoiceAsync(id, "INV001240");

        Assert.Equal(ChangeInvoiceOutcome.NotAPod, result.Outcome);
        Assert.Empty(_acumatica.Lookups);
        Assert.Null((await LoadAsync(id)).MatchedReference);
    }

    [Fact]
    public async Task ChangeInvoice_UnknownDocument_NotFound()
    {
        var result = await ChangeInvoiceAsync(Guid.NewGuid(), "INV001240");

        Assert.Equal(ChangeInvoiceOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task ChangeInvoice_NoExactInvoice_InvoiceNotFound_NoWrite()
    {
        var id = await SeedAsync(DocumentState.Filed, matchedReference: "INV001234");
        // A prefix hit is not the invoice: only an exact reference counts.
        _acumatica.SearchResults = [Invoice("INV0012400", "Midwater Spar")];

        var result = await ChangeInvoiceAsync(id, "INV001240");

        Assert.Equal(ChangeInvoiceOutcome.InvoiceNotFound, result.Outcome);
        Assert.Equal("INV001234", (await LoadAsync(id)).MatchedReference);
        Assert.Empty(await DocumentAuditsAsync());
    }

    [Theory]
    [InlineData("INV001234")]
    [InlineData(" inv001234 ")]
    public async Task ChangeInvoice_SameInvoice_NoLookup_NoWrite(string reference)
    {
        var id = await SeedAsync(DocumentState.Filed, matchedReference: "INV001234");
        _acumatica.SearchResults = [Invoice("INV001234", "Acme Stores (Pty) Ltd")];

        var result = await ChangeInvoiceAsync(id, reference);

        Assert.Equal(ChangeInvoiceOutcome.SameInvoice, result.Outcome);
        Assert.Empty(_acumatica.Lookups);
        Assert.Empty(await DocumentAuditsAsync());
    }

    [Fact]
    public async Task ChangeInvoice_RowChangedDuringTheLookup_ChangedMeanwhile_KeepsTheOtherChange_NoAudit()
    {
        var id = await SeedAsync(DocumentState.Filed, matchedReference: "INV001234");
        _acumatica.SearchResults = [Invoice("INV001240", "Midwater Spar")];
        _acumatica.OnSearch = async () =>
        {
            // Another person changes the invoice while this request looks up its own.
            await using var other = CreateDbContext();
            await other.CapturedDocuments.Where(item => item.Id == id).ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.MatchedReference, "INV777"));
        };

        var result = await ChangeInvoiceAsync(id, "INV001240");

        Assert.Equal(ChangeInvoiceOutcome.ChangedMeanwhile, result.Outcome);
        Assert.Equal("INV777", (await LoadAsync(id)).MatchedReference);
        Assert.Empty(await DocumentAuditsAsync());
    }

    [Fact]
    public async Task ChangeInvoice_AttachedDuringTheLookup_Attached_NoWrite()
    {
        var id = await SeedAsync(DocumentState.Filed, matchedReference: "INV001234");
        _acumatica.SearchResults = [Invoice("INV001240", "Midwater Spar")];
        _acumatica.OnSearch = async () =>
        {
            await using var other = CreateDbContext();
            await other.CapturedDocuments.Where(item => item.Id == id).ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.AcumaticaAttachedAt, DateTimeOffset.UtcNow));
        };

        var result = await ChangeInvoiceAsync(id, "INV001240");

        Assert.Equal(ChangeInvoiceOutcome.Attached, result.Outcome);
        Assert.Equal("INV001234", (await LoadAsync(id)).MatchedReference);
        Assert.Empty(await DocumentAuditsAsync());
    }

    [Fact]
    public async Task ChangeInvoice_SentBackToReviewDuringTheLookup_NotFiled_NoWrite()
    {
        var id = await SeedAsync(DocumentState.Filed, matchedReference: "INV001234");
        _acumatica.SearchResults = [Invoice("INV001240", "Midwater Spar")];
        _acumatica.OnSearch = async () =>
        {
            await using var other = CreateDbContext();
            await other.CapturedDocuments.Where(item => item.Id == id).ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.State, DocumentState.NeedsReview)
                .SetProperty(item => item.MatchedReference, (string?)null));
        };

        var result = await ChangeInvoiceAsync(id, "INV001240");

        Assert.Equal(ChangeInvoiceOutcome.NotFiled, result.Outcome);
        Assert.Null((await LoadAsync(id)).MatchedReference);
    }

    [Theory]
    [InlineData("POD", DocumentState.Filed, false, true)]
    [InlineData("POD", DocumentState.Filed, true, false)]
    [InlineData("POD", DocumentState.NeedsReview, false, false)]
    [InlineData("GENERAL", DocumentState.Filed, false, false)]
    public void ChangeInvoiceCheck_FollowsTheRule(string type, DocumentState state, bool attached, bool expected)
    {
        var outcome = DocumentReviewService.ChangeInvoiceCheck(type, state, attached ? DateTimeOffset.UtcNow : null);

        Assert.Equal(expected, outcome == ChangeInvoiceOutcome.Changed);
    }

    [Fact]
    public async Task Process_AfterDelete_CompletesQuietly()
    {
        var id = await SeedAsync(DocumentState.Failed);
        Assert.Equal(DeleteOutcome.Deleted, (await DeleteAsync(id, "Duplicate")).Outcome);

        await using (var db = CreateDbContext())
        {
            // A later document.received.v1 for the deleted document: nothing to claim, no throw.
            await CreateProcessor(db, AcumaticaAttachMode.Write).ProcessAsync(id, default);
        }

        Assert.Null(await FindAsync(id));
        Assert.Empty(_acumatica.AttachAttempts);
    }

    private async Task<ChangeInvoiceResult> ChangeInvoiceAsync(Guid id, string reference, string? reason = null)
    {
        await using var db = CreateDbContext();
        return await CreateService(db, AcumaticaAttachMode.Shadow).ChangeInvoiceAsync(id, reference, reason, User, default);
    }

    private async Task<List<AuditEventEntity>> DocumentAuditsAsync()
    {
        await using var check = CreateDbContext();
        return await check.AuditEvents.AsNoTracking().Where(audit => audit.EntityType == "CapturedDocument").ToListAsync();
    }

    private async Task<DeleteResult> DeleteAsync(Guid id, string reason)
    {
        await using var db = CreateDbContext();
        return await CreateService(db, AcumaticaAttachMode.Shadow).DeleteAsync(id, reason, User, default);
    }

    private async Task AssertUntouchedAsync(Guid id, DocumentState state)
    {
        var row = await LoadAsync(id);
        Assert.Equal(state, row.State);
        Assert.True(_store.Blobs.ContainsKey(row.BlobPath));
        Assert.Empty(_store.Deleted);
        await using var check = CreateDbContext();
        Assert.Empty(await check.AuditEvents.AsNoTracking().Where(audit => audit.EntityType == "CapturedDocument").ToListAsync());
    }

    private async Task<CapturedDocumentEntity?> FindAsync(Guid id)
    {
        await using var db = CreateDbContext();
        return await db.CapturedDocuments.AsNoTracking().SingleOrDefaultAsync(document => document.Id == id);
    }

    private async Task<Guid> SeedDeviceAsync(string name)
    {
        var id = Guid.NewGuid();
        await using var db = CreateDbContext();
        db.ScannerDevices.Add(new ScannerDeviceEntity
        {
            Id = id,
            Name = name,
            CreatedBy = User,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<ProcessAgainOutcome> ProcessAgainAsync(Guid id)
    {
        await using var db = CreateDbContext();
        return await CreateService(db, AcumaticaAttachMode.Shadow).ProcessAgainAsync(id, User, default);
    }

    private async Task<ReturnToReviewOutcome> ReturnToReviewAsync(Guid id, string note, string user)
    {
        await using var db = CreateDbContext();
        return await CreateService(db, AcumaticaAttachMode.Shadow).ReturnToReviewAsync(id, note, user, default);
    }

    private async Task<FileToResult> FileToAsync(Guid id, string reference, AcumaticaAttachMode mode)
    {
        await using var db = CreateDbContext();
        return await CreateService(db, mode).FileToAsync(id, reference, User, default);
    }

    private DocumentReviewService CreateService(PvmDbContext db, AcumaticaAttachMode mode)
        => new(db, _acumatica, CreateProcessor(db, mode), new IntegrationCommandQueue(db), _store, NullLogger<DocumentReviewService>.Instance);

    private DocumentProcessor CreateProcessor(PvmDbContext db, AcumaticaAttachMode mode)
        => new(
            db,
            [new ProofOfDeliveryDocumentType()],
            new UnusedExtractor(),
            _acumatica,
            _store,
            Options.Create(new DocumentCaptureOptions { AcumaticaAttachMode = mode }),
            NullLogger<DocumentProcessor>.Instance);

    private static InvoiceCandidate Invoice(string reference, string customerName)
        => new(reference, new DateOnly(2026, 11, 1), "C001", customerName, null, null, "Open");

    private async Task<Guid> SeedAsync(
        DocumentState state,
        string? matchedReference = null,
        string? reviewNote = null,
        DateTimeOffset? uploadedAt = null,
        string? extractionJson = null,
        DateTimeOffset? acumaticaAttachedAt = null,
        Guid? scannerDeviceId = null,
        string? scannerStaffName = null,
        string documentType = "POD",
        string? title = null,
        bool thumbnail = false,
        string? matchedCustomerName = null,
        bool attachPending = false)
    {
        var id = Guid.NewGuid();
        var at = uploadedAt ?? DateTimeOffset.UtcNow;
        var blobPath = $"documents/{documentType}/{at:yyyy}/{at:MM}/{id:D}.pdf";
        var thumbnailPath = thumbnail ? $"documents/{documentType}/{at:yyyy}/{at:MM}/{id:D}.thumb.jpg" : null;
        _store.Blobs[blobPath] = PdfBytes;
        if (thumbnailPath is not null)
        {
            _store.Blobs[thumbnailPath] = [0xFF, 0xD8, 0xFF];
        }

        await using var db = CreateDbContext();
        db.CapturedDocuments.Add(new CapturedDocumentEntity
        {
            Id = id,
            ClientDocumentId = Guid.NewGuid(),
            DocumentType = documentType,
            State = state,
            ReviewReason = state == DocumentState.NeedsReview ? ReviewReason.NoSecondSignal : null,
            BlobPath = blobPath,
            ThumbnailBlobPath = thumbnailPath,
            Title = title,
            PageCount = 1,
            SizeBytes = PdfBytes.Length,
            Sha256 = new string('a', 64),
            MatchedReference = matchedReference,
            MatchedCustomerName = matchedCustomerName,
            MatchedBy = matchedReference is null ? null : "auto",
            AcumaticaAttachPending = attachPending,
            ReviewNote = reviewNote,
            ExtractionJson = extractionJson,
            AcumaticaAttachedAt = acumaticaAttachedAt,
            ScannerDeviceId = scannerDeviceId,
            ScannerStaffName = scannerStaffName,
            UploadedBy = "driver@pvm.co.za",
            UploadedAt = at,
            UpdatedAt = at,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<CapturedDocumentEntity> LoadAsync(Guid id)
    {
        await using var db = CreateDbContext();
        return await db.CapturedDocuments.AsNoTracking().SingleAsync(document => document.Id == id);
    }

    private PvmDbContext CreateDbContext(IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<PvmDbContext>()
            .UseNpgsql(_postgres.GetConnectionString());
        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new PvmDbContext(builder.Options);
    }

    /// <summary>Runs the race once, just before the first DELETE statement goes to the database.</summary>
    private sealed class BeforeDeleteInterceptor(Func<Task> race) : DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Fired && command.CommandText.TrimStart().StartsWith("DELETE", StringComparison.OrdinalIgnoreCase))
            {
                Fired = true;
                await race();
            }

            return result;
        }
    }

    private sealed class UnusedExtractor : IDocumentExtractor
    {
        public Task<ExtractionResult> ExtractAsync(IDocumentType type, Stream pdf, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class FakeAcumatica : IAcumaticaDocumentClient
    {
        public IReadOnlyList<InvoiceCandidate> SearchResults { get; set; } = [];

        public bool AttachFails { get; set; }

        public Func<Task>? OnSearch { get; set; }

        public List<string> Queries { get; } = [];

        /// <summary>The exact-reference lookups; they answer from <see cref="SearchResults"/>.</summary>
        public List<string> Lookups { get; } = [];

        public List<string> AttachAttempts { get; } = [];

        public List<(string Entity, string ReferenceNumber, string FileName, byte[] Bytes)> Attached { get; } = [];

        public Task<IReadOnlyList<CandidateHit>> FindInvoicesAsync(PodExtraction extraction, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<InvoiceCandidate>> SearchInvoicesAsync(string query, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            return Task.FromResult(SearchResults);
        }

        public async Task<InvoiceCandidate?> GetInvoiceAsync(string referenceNumber, CancellationToken cancellationToken)
        {
            Lookups.Add(referenceNumber);
            if (OnSearch is not null)
            {
                await OnSearch();
            }

            return SearchResults.FirstOrDefault(invoice =>
                string.Equals(invoice.ReferenceNumber, referenceNumber, StringComparison.OrdinalIgnoreCase));
        }

        public async Task AttachFileAsync(string entity, string referenceNumber, string fileName, Stream pdf, CancellationToken cancellationToken)
        {
            AttachAttempts.Add(referenceNumber);
            if (AttachFails)
            {
                throw new HttpRequestException("Acumatica refused the file.");
            }

            using var buffer = new MemoryStream();
            await pdf.CopyToAsync(buffer, cancellationToken);
            Attached.Add((entity, referenceNumber, fileName, buffer.ToArray()));
        }
    }

    private sealed class FakeStore : IDocumentStore
    {
        public Dictionary<string, byte[]> Blobs { get; } = new(StringComparer.Ordinal);

        public List<string> Deleted { get; } = [];

        public bool DeleteFails { get; set; }

        /// <summary>When set, only the delete of a path that matches fails.</summary>
        public Func<string, bool>? DeleteFailsFor { get; set; }

        public Task DeleteAsync(string blobPath, CancellationToken cancellationToken)
        {
            if (DeleteFails || DeleteFailsFor?.Invoke(blobPath) == true)
            {
                throw new IOException("Storage refused the delete.");
            }

            Deleted.Add(blobPath);
            Blobs.Remove(blobPath);
            return Task.CompletedTask;
        }

        public Task<StoredDocument> SaveAsync(string blobPath, Stream pdf, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(string blobPath, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new MemoryStream(Blobs[blobPath], writable: false));
    }
}
