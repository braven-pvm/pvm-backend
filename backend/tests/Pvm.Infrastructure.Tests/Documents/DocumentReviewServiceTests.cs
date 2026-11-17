using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pvm.Application.Documents;
using Pvm.Infrastructure.Documents;
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
        Assert.Equal(["inv001234"], _acumatica.Queries);
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
        Assert.Empty(_acumatica.Queries);
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

    private async Task<FileToResult> FileToAsync(Guid id, string reference, AcumaticaAttachMode mode)
    {
        await using var db = CreateDbContext();
        return await CreateService(db, mode).FileToAsync(id, reference, User, default);
    }

    private DocumentReviewService CreateService(PvmDbContext db, AcumaticaAttachMode mode)
    {
        var options = Options.Create(new DocumentCaptureOptions { AcumaticaAttachMode = mode });
        var processor = new DocumentProcessor(
            db,
            [new ProofOfDeliveryDocumentType()],
            new UnusedExtractor(),
            _acumatica,
            _store,
            options,
            NullLogger<DocumentProcessor>.Instance);
        return new DocumentReviewService(db, _acumatica, processor, NullLogger<DocumentReviewService>.Instance);
    }

    private static InvoiceCandidate Invoice(string reference, string customerName)
        => new(reference, new DateOnly(2026, 11, 1), "C001", customerName, null, null, "Open");

    private async Task<Guid> SeedAsync(
        DocumentState state,
        string? matchedReference = null,
        string? reviewNote = null)
    {
        var id = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow;
        var blobPath = $"documents/POD/{at:yyyy}/{at:MM}/{id:D}.pdf";
        _store.Blobs[blobPath] = PdfBytes;
        await using var db = CreateDbContext();
        db.CapturedDocuments.Add(new CapturedDocumentEntity
        {
            Id = id,
            ClientDocumentId = Guid.NewGuid(),
            DocumentType = "POD",
            State = state,
            ReviewReason = state == DocumentState.NeedsReview ? ReviewReason.NoSecondSignal : null,
            BlobPath = blobPath,
            PageCount = 1,
            SizeBytes = PdfBytes.Length,
            Sha256 = new string('a', 64),
            MatchedReference = matchedReference,
            ReviewNote = reviewNote,
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

    private PvmDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PvmDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new PvmDbContext(options);
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

        public List<string> AttachAttempts { get; } = [];

        public List<(string Entity, string ReferenceNumber, string FileName, byte[] Bytes)> Attached { get; } = [];

        public Task<IReadOnlyList<CandidateHit>> FindInvoicesAsync(PodExtraction extraction, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public async Task<IReadOnlyList<InvoiceCandidate>> SearchInvoicesAsync(string query, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            if (OnSearch is not null)
            {
                await OnSearch();
            }

            return SearchResults;
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

        public Task<StoredDocument> SaveAsync(string blobPath, Stream pdf, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(string blobPath, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new MemoryStream(Blobs[blobPath], writable: false));
    }
}
