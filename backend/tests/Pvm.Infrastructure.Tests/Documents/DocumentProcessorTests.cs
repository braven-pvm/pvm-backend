using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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

public sealed class DocumentProcessorTests : IAsyncLifetime
{
    private const string MatchingFields = """
        {
          "InvoiceNumbers": ["INV001234"],
          "SalesOrderNumbers": [],
          "CustomerOrderNumbers": [],
          "ClientName": "Acme Stores",
          "DeliveryDate": "2026-11-02",
          "SignaturePresent": true,
          "Note": null
        }
        """;

    private const string NoClientNameFields = """
        {
          "invoiceNumbers": ["INV001234"],
          "salesOrderNumbers": [],
          "customerOrderNumbers": [],
          "clientName": null,
          "deliveryDate": "2026-11-02",
          "signaturePresent": true,
          "note": "stamp smudged"
        }
        """;

    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7 fake scan");

    private static readonly InvoiceCandidate Invoice = new(
        "INV001234",
        new DateOnly(2026, 11, 1),
        "C001",
        "Acme Stores (Pty) Ltd",
        null,
        null,
        "Open");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();
    private readonly FakeExtractor _extractor = new();
    private readonly FakeAcumatica _acumatica = new();
    private readonly FakeStore _store = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await DatabaseMigrationRunner.MigrateAsync(db);
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Process_SafeMatch_ShadowMode_FiledWithAttachPending_NoAcumaticaCall()
    {
        var id = await SeedAsync();
        _extractor.Result = Fields(MatchingFields);
        _acumatica.Hits = [new CandidateHit(Invoice, "invoice")];

        await using (var db = CreateDbContext())
        {
            await CreateProcessor(db, AcumaticaAttachMode.Shadow).ProcessAsync(id, default);
        }

        var row = await LoadAsync(id);
        Assert.Equal(DocumentState.Filed, row.State);
        Assert.Null(row.ReviewReason);
        Assert.Equal("INV001234", row.MatchedReference);
        Assert.Equal("Acme Stores (Pty) Ltd", row.MatchedCustomerName);
        Assert.Equal("auto", row.MatchedBy);
        Assert.True(row.AcumaticaAttachPending);
        Assert.Null(row.AcumaticaAttachedAt);
        Assert.NotNull(row.ExtractionJson);
        Assert.Empty(_acumatica.Attached);
    }

    [Fact]
    public async Task Process_SafeMatch_WriteMode_FiledAndAttached()
    {
        var id = await SeedAsync();
        _extractor.Result = Fields(MatchingFields);
        _acumatica.Hits = [new CandidateHit(Invoice, "invoice")];

        await using (var db = CreateDbContext())
        {
            await CreateProcessor(db, AcumaticaAttachMode.Write).ProcessAsync(id, default);
        }

        var row = await LoadAsync(id);
        Assert.Equal(DocumentState.Filed, row.State);
        Assert.False(row.AcumaticaAttachPending);
        Assert.NotNull(row.AcumaticaAttachedAt);
        var attach = Assert.Single(_acumatica.Attached);
        Assert.Equal("SalesInvoice", attach.Entity);
        Assert.Equal("INV001234", attach.ReferenceNumber);
        Assert.Equal($"POD-INV001234-{id:D}.pdf", attach.FileName);
        Assert.Equal(PdfBytes, attach.Bytes);
    }

    [Fact]
    public async Task Process_WriteMode_AttachFails_FiledWithAttachPending()
    {
        var id = await SeedAsync();
        _extractor.Result = Fields(MatchingFields);
        _acumatica.Hits = [new CandidateHit(Invoice, "invoice")];
        _acumatica.AttachFailures = 1;

        await using (var db = CreateDbContext())
        {
            await CreateProcessor(db, AcumaticaAttachMode.Write).ProcessAsync(id, default);
        }

        var row = await LoadAsync(id);
        Assert.Equal(DocumentState.Filed, row.State);
        Assert.Equal("INV001234", row.MatchedReference);
        Assert.True(row.AcumaticaAttachPending);
        Assert.Null(row.AcumaticaAttachedAt);
    }

    [Fact]
    public async Task Process_SecondPodForSameInvoice_IsFiled()
    {
        _extractor.Result = Fields(MatchingFields);
        _acumatica.Hits = [new CandidateHit(Invoice, "invoice")];
        var first = await SeedAsync();
        var second = await SeedAsync();

        await using (var db = CreateDbContext())
        {
            await CreateProcessor(db, AcumaticaAttachMode.Shadow).ProcessAsync(first, default);
            await CreateProcessor(db, AcumaticaAttachMode.Shadow).ProcessAsync(second, default);
        }

        Assert.Equal(DocumentState.Filed, (await LoadAsync(first)).State);
        var row = await LoadAsync(second);
        Assert.Equal(DocumentState.Filed, row.State);
        Assert.Equal("INV001234", row.MatchedReference);
    }

    [Fact]
    public async Task Process_Unreadable_NeedsReviewReadFailure()
    {
        var id = await SeedAsync();
        _extractor.Result = new Unreadable("refusal");

        await using (var db = CreateDbContext())
        {
            await CreateProcessor(db, AcumaticaAttachMode.Write).ProcessAsync(id, default);
        }

        var row = await LoadAsync(id);
        Assert.Equal(DocumentState.NeedsReview, row.State);
        Assert.Equal(ReviewReason.ReadFailure, row.ReviewReason);
        using var json = JsonDocument.Parse(row.ExtractionJson!);
        Assert.Equal("refusal", json.RootElement.GetProperty("unreadable").GetString());
        Assert.Equal(0, _acumatica.FindCalls);
    }

    [Fact]
    public async Task Process_FieldsDoNotDeserialize_NeedsReviewReadFailure_RawSaved()
    {
        var id = await SeedAsync();
        _extractor.Result = Fields("""{ "InvoiceNumbers": "not-a-list", "DeliveryDate": "02/11/2026" }""");

        await using (var db = CreateDbContext())
        {
            await CreateProcessor(db, AcumaticaAttachMode.Write).ProcessAsync(id, default);
        }

        var row = await LoadAsync(id);
        Assert.Equal(DocumentState.NeedsReview, row.State);
        Assert.Equal(ReviewReason.ReadFailure, row.ReviewReason);
        using var json = JsonDocument.Parse(row.ExtractionJson!);
        Assert.Equal("not-a-list", json.RootElement.GetProperty("InvoiceNumbers").GetString());
        Assert.Equal(0, _acumatica.FindCalls);
    }

    [Fact]
    public async Task Process_UnknownDocumentType_NeedsReviewReadFailure()
    {
        var id = await SeedAsync(documentType: "GRN");

        await using (var db = CreateDbContext())
        {
            await CreateProcessor(db, AcumaticaAttachMode.Write).ProcessAsync(id, default);
        }

        var row = await LoadAsync(id);
        Assert.Equal(DocumentState.NeedsReview, row.State);
        Assert.Equal(ReviewReason.ReadFailure, row.ReviewReason);
        Assert.Equal(0, _extractor.Calls);
    }

    [Fact]
    public async Task Process_NoSecondSignal_NeedsReview_ExtractionSaved()
    {
        var id = await SeedAsync();
        _extractor.Result = Fields(NoClientNameFields);
        _acumatica.Hits = [new CandidateHit(Invoice, "invoice")];

        await using (var db = CreateDbContext())
        {
            await CreateProcessor(db, AcumaticaAttachMode.Write).ProcessAsync(id, default);
        }

        var row = await LoadAsync(id);
        Assert.Equal(DocumentState.NeedsReview, row.State);
        Assert.Equal(ReviewReason.NoSecondSignal, row.ReviewReason);
        Assert.Null(row.MatchedReference);
        Assert.False(row.AcumaticaAttachPending);
        Assert.Empty(_acumatica.Attached);

        var lookedUp = Assert.Single(_acumatica.Extractions);
        Assert.Equal(["INV001234"], lookedUp.InvoiceNumbers);
        Assert.Equal(new DateOnly(2026, 11, 2), lookedUp.DeliveryDate);
        Assert.Equal("stamp smudged", lookedUp.Note);

        using var json = JsonDocument.Parse(row.ExtractionJson!);
        Assert.Equal("stamp smudged", json.RootElement.GetProperty("note").GetString());
        var candidate = Assert.Single(json.RootElement.GetProperty("candidates").EnumerateArray());
        Assert.Equal("INV001234", candidate.GetProperty("referenceNumber").GetString());
        Assert.Equal("Acme Stores (Pty) Ltd", candidate.GetProperty("customerName").GetString());
    }

    [Fact]
    public async Task Process_AlreadyFiled_DoesNothing()
    {
        var id = await SeedAsync();
        _extractor.Result = Fields(MatchingFields);
        _acumatica.Hits = [new CandidateHit(Invoice, "invoice")];

        await using (var db = CreateDbContext())
        {
            await CreateProcessor(db, AcumaticaAttachMode.Write).ProcessAsync(id, default);
        }

        await using (var db = CreateDbContext())
        {
            await CreateProcessor(db, AcumaticaAttachMode.Write).ProcessAsync(id, default);
        }

        Assert.Equal(1, _extractor.Calls);
        Assert.Equal(1, _acumatica.FindCalls);
        Assert.Single(_acumatica.Attached);
    }

    [Theory]
    [InlineData(DocumentState.NeedsReview)]
    [InlineData(DocumentState.Failed)]
    public async Task Process_TerminalState_DoesNothing(DocumentState state)
    {
        var id = await SeedAsync(state: state);

        await using (var db = CreateDbContext())
        {
            await CreateProcessor(db, AcumaticaAttachMode.Write).ProcessAsync(id, default);
        }

        Assert.Equal(state, (await LoadAsync(id)).State);
        Assert.Equal(0, _extractor.Calls);
    }

    [Fact]
    public async Task Process_LeftInProcessing_IsProcessedAgain()
    {
        var id = await SeedAsync(state: DocumentState.Processing);
        _extractor.Result = Fields(MatchingFields);
        _acumatica.Hits = [new CandidateHit(Invoice, "invoice")];

        await using (var db = CreateDbContext())
        {
            await CreateProcessor(db, AcumaticaAttachMode.Shadow).ProcessAsync(id, default);
        }

        Assert.Equal(DocumentState.Filed, (await LoadAsync(id)).State);
    }

    [Fact]
    public async Task Process_AcumaticaLookupThrows_Rethrows_SoServiceBusRetries()
    {
        var id = await SeedAsync();
        _extractor.Result = Fields(MatchingFields);
        _acumatica.FindException = new HttpRequestException("Acumatica is down");

        await using (var db = CreateDbContext())
        {
            await Assert.ThrowsAsync<HttpRequestException>(
                () => CreateProcessor(db, AcumaticaAttachMode.Write).ProcessAsync(id, default));
        }

        var row = await LoadAsync(id);
        Assert.Equal(DocumentState.Processing, row.State);
        Assert.NotNull(row.ExtractionJson);
    }

    [Fact]
    public async Task ProcessMessage_LookupThrows_BeforeLastDelivery_Abandoned_RowProcessing()
    {
        var id = await SeedAsync();
        _extractor.Result = Fields(MatchingFields);
        _acumatica.FindException = new HttpRequestException("Acumatica is down");

        MessageProcessingResult result;
        await using (var db = CreateDbContext())
        {
            result = await ProcessMessageAsync(db, id, deliveryCount: 1);
        }

        Assert.Equal(MessageSettlement.Abandon, result.Settlement);
        Assert.Equal(DocumentState.Processing, (await LoadAsync(id)).State);
    }

    [Fact]
    public async Task ProcessMessage_LookupThrows_OnLastDelivery_DeadLettered_RowFailed()
    {
        var id = await SeedAsync();
        _extractor.Result = Fields(MatchingFields);
        _acumatica.FindException = new HttpRequestException("Acumatica is down");

        MessageProcessingResult result;
        await using (var db = CreateDbContext())
        {
            result = await ProcessMessageAsync(db, id, deliveryCount: 5);
        }

        Assert.Equal(MessageSettlement.DeadLetter, result.Settlement);
        Assert.Equal("max-delivery-count", result.DeadLetterReason);
        Assert.Equal(DocumentState.Failed, (await LoadAsync(id)).State);
    }

    [Fact]
    public async Task ProcessMessage_JsonExceptionFromProcessing_IsTransient_Abandoned_RowProcessing()
    {
        var id = await SeedAsync();
        _extractor.Result = Fields(MatchingFields);
        _acumatica.FindException = new JsonException("Acumatica returned HTML.");

        MessageProcessingResult result;
        await using (var db = CreateDbContext())
        {
            result = await ProcessMessageAsync(db, id, deliveryCount: 1);
        }

        Assert.Equal(MessageSettlement.Abandon, result.Settlement);
        Assert.Equal(DocumentState.Processing, (await LoadAsync(id)).State);
    }

    [Fact]
    public async Task ProcessMessage_EmptyDocumentId_IsInvalidMessageData_DeadLettered()
    {
        MessageProcessingResult result;
        await using (var db = CreateDbContext())
        {
            result = await ProcessMessageAsync(db, Guid.Empty, deliveryCount: 1);
        }

        Assert.Equal(MessageSettlement.DeadLetter, result.Settlement);
        Assert.Equal("invalid-message-data", result.DeadLetterReason);
    }

    [Fact]
    public async Task ProcessMessage_LastDelivery_MarkFailedThrows_StillDeadLettered()
    {
        var id = await SeedAsync();
        var brokenDb = CreateDbContext();
        await brokenDb.DisposeAsync();

        MessageProcessingResult result;
        await using (var db = CreateDbContext())
        {
            // The processor's own DbContext is gone: ProcessAsync and MarkFailedAsync both throw.
            result = await ProcessMessageAsync(db, id, deliveryCount: 5, documentDb: brokenDb);
        }

        Assert.Equal(MessageSettlement.DeadLetter, result.Settlement);
        Assert.Equal("max-delivery-count", result.DeadLetterReason);
        Assert.Equal(DocumentState.Received, (await LoadAsync(id)).State);
    }

    [Fact]
    public async Task Process_RowResolvedByPersonDuringRun_IsNotOverwritten()
    {
        var id = await SeedAsync();
        _extractor.Result = Fields(MatchingFields);
        _acumatica.Hits = [new CandidateHit(Invoice, "invoice")];
        _extractor.OnExtract = async () =>
        {
            await using var other = CreateDbContext();
            await other.CapturedDocuments
                .Where(document => document.Id == id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(document => document.State, DocumentState.Filed)
                    .SetProperty(document => document.MatchedReference, "INV009999")
                    .SetProperty(document => document.MatchedBy, "clerk@pvm.co.za"));
        };

        await using (var db = CreateDbContext())
        {
            await CreateProcessor(db, AcumaticaAttachMode.Write).ProcessAsync(id, default);
        }

        var row = await LoadAsync(id);
        Assert.Equal(DocumentState.Filed, row.State);
        Assert.Equal("INV009999", row.MatchedReference);
        Assert.Equal("clerk@pvm.co.za", row.MatchedBy);
        Assert.Null(row.ExtractionJson);
        Assert.Equal(0, _acumatica.FindCalls);
        Assert.Empty(_acumatica.AttachAttempts);
    }

    [Fact]
    public async Task Process_RowResolvedByPersonDuringLookup_ReviewNotWritten()
    {
        var id = await SeedAsync();
        _extractor.Result = Fields(NoClientNameFields);
        _acumatica.Hits = [new CandidateHit(Invoice, "invoice")];
        _acumatica.OnFind = async () =>
        {
            await using var other = CreateDbContext();
            await other.CapturedDocuments
                .Where(document => document.Id == id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(document => document.State, DocumentState.Filed)
                    .SetProperty(document => document.MatchedReference, "INV009999"));
        };

        await using (var db = CreateDbContext())
        {
            await CreateProcessor(db, AcumaticaAttachMode.Write).ProcessAsync(id, default);
        }

        var row = await LoadAsync(id);
        Assert.Equal(DocumentState.Filed, row.State);
        Assert.Null(row.ReviewReason);
        Assert.Equal("INV009999", row.MatchedReference);
    }

    [Fact]
    public async Task ProcessMessage_SafeMatch_Completed()
    {
        var id = await SeedAsync();
        _extractor.Result = Fields(MatchingFields);
        _acumatica.Hits = [new CandidateHit(Invoice, "invoice")];

        MessageProcessingResult result;
        await using (var db = CreateDbContext())
        {
            result = await ProcessMessageAsync(db, id, deliveryCount: 1);
        }

        Assert.Equal(MessageSettlement.Complete, result.Settlement);
        Assert.Equal(DocumentState.Filed, (await LoadAsync(id)).State);
    }

    [Fact]
    public async Task Backfill_WriteMode_AttachesPendingDocuments_ClearsFlag()
    {
        var older = await SeedAsync(state: DocumentState.Filed, matchedReference: "INV000001", attachPending: true, uploadedAt: DateTimeOffset.UtcNow.AddHours(-2));
        var newer = await SeedAsync(state: DocumentState.Filed, matchedReference: "INV000002", attachPending: true, uploadedAt: DateTimeOffset.UtcNow.AddHours(-1));
        var failing = await SeedAsync(state: DocumentState.Filed, matchedReference: "INV000003", attachPending: true, uploadedAt: DateTimeOffset.UtcNow.AddHours(-3));
        var review = await SeedAsync(state: DocumentState.NeedsReview, attachPending: true);
        _acumatica.FailFor.Add("INV000003");

        int attached;
        await using (var db = CreateDbContext())
        {
            attached = await CreateProcessor(db, AcumaticaAttachMode.Write).BackfillPendingAttachmentsAsync(default);
        }

        Assert.Equal(2, attached);
        Assert.Equal(["INV000003", "INV000001", "INV000002"], _acumatica.AttachAttempts);
        Assert.Equal($"POD-INV000001-{older:D}.pdf", _acumatica.Attached[0].FileName);

        foreach (var id in new[] { older, newer })
        {
            var row = await LoadAsync(id);
            Assert.False(row.AcumaticaAttachPending);
            Assert.NotNull(row.AcumaticaAttachedAt);
        }

        var failed = await LoadAsync(failing);
        Assert.True(failed.AcumaticaAttachPending);
        Assert.NotNull(failed.AcumaticaAttachLastAttemptAt);
        Assert.True((await LoadAsync(review)).AcumaticaAttachPending);
    }

    [Fact]
    public async Task Backfill_WriteMode_FailingDocumentsDoNotBlockOthers()
    {
        for (var i = 0; i < 20; i++)
        {
            var reference = $"BAD{i:D6}";
            _acumatica.FailFor.Add(reference);
            await SeedAsync(state: DocumentState.Filed, matchedReference: reference, attachPending: true, uploadedAt: DateTimeOffset.UtcNow.AddDays(-1));
        }

        var good = await SeedAsync(state: DocumentState.Filed, matchedReference: "INV000001", attachPending: true);

        var total = 0;
        for (var run = 0; run < 2; run++)
        {
            await using var db = CreateDbContext();
            total += await CreateProcessor(db, AcumaticaAttachMode.Write).BackfillPendingAttachmentsAsync(default);
        }

        Assert.Equal(1, total);
        Assert.False((await LoadAsync(good)).AcumaticaAttachPending);
        Assert.Equal(21, _acumatica.AttachAttempts.Count);
    }

    [Fact]
    public async Task Backfill_WriteMode_UnknownType_RecordsAttempt()
    {
        var id = await SeedAsync(state: DocumentState.Filed, documentType: "GRN", matchedReference: "INV000001", attachPending: true);

        await using (var db = CreateDbContext())
        {
            Assert.Equal(0, await CreateProcessor(db, AcumaticaAttachMode.Write).BackfillPendingAttachmentsAsync(default));
        }

        var row = await LoadAsync(id);
        Assert.True(row.AcumaticaAttachPending);
        Assert.NotNull(row.AcumaticaAttachLastAttemptAt);
        Assert.Empty(_acumatica.AttachAttempts);
    }

    [Fact]
    public async Task Backfill_WriteMode_TakesBatchOfTwenty()
    {
        for (var i = 0; i < 21; i++)
        {
            await SeedAsync(state: DocumentState.Filed, matchedReference: $"INV{i:D6}", attachPending: true);
        }

        await using var db = CreateDbContext();
        var attached = await CreateProcessor(db, AcumaticaAttachMode.Write).BackfillPendingAttachmentsAsync(default);

        Assert.Equal(20, attached);
        Assert.Equal(1, await db.CapturedDocuments.CountAsync(document => document.AcumaticaAttachPending));
    }

    [Fact]
    public async Task Backfill_ShadowMode_DoesNothing()
    {
        var id = await SeedAsync(state: DocumentState.Filed, matchedReference: "INV000001", attachPending: true);

        int attached;
        await using (var db = CreateDbContext())
        {
            attached = await CreateProcessor(db, AcumaticaAttachMode.Shadow).BackfillPendingAttachmentsAsync(default);
        }

        Assert.Equal(0, attached);
        Assert.Empty(_acumatica.AttachAttempts);
        Assert.True((await LoadAsync(id)).AcumaticaAttachPending);
    }

    private Task<MessageProcessingResult> ProcessMessageAsync(
        PvmDbContext db,
        Guid documentId,
        int deliveryCount,
        PvmDbContext? documentDb = null)
    {
        var messageId = Guid.NewGuid();
        var envelope = new IntegrationMessageEnvelope(
            messageId,
            IntegrationMessageTypes.DocumentReceivedV1,
            documentId.ToString("D"),
            null,
            DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(
                new ProcessCapturedDocumentMessage(documentId),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var payload = JsonSerializer.Serialize(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var processor = new IntegrationMessageProcessor(
            new IntegrationDeliveryRepository(db),
            null!,
            null!,
            null!,
            null!,
            CreateProcessor(documentDb ?? db, AcumaticaAttachMode.Shadow));
        return processor.ProcessAsync(
            IntegrationQueues.DocumentProcessing,
            messageId.ToString("D"),
            payload,
            deliveryCount,
            5,
            DateTimeOffset.UtcNow,
            default);
    }

    private DocumentProcessor CreateProcessor(PvmDbContext db, AcumaticaAttachMode mode)
        => new(
            db,
            [new ProofOfDeliveryDocumentType()],
            _extractor,
            _acumatica,
            _store,
            Options.Create(new DocumentCaptureOptions { AcumaticaAttachMode = mode }),
            NullLogger<DocumentProcessor>.Instance);

    private async Task<Guid> SeedAsync(
        DocumentState state = DocumentState.Received,
        string documentType = "POD",
        string? matchedReference = null,
        bool attachPending = false,
        DateTimeOffset? uploadedAt = null)
    {
        var id = Guid.NewGuid();
        var at = uploadedAt ?? DateTimeOffset.UtcNow;
        var blobPath = $"documents/{documentType}/{at:yyyy}/{at:MM}/{id:D}.pdf";
        _store.Blobs[blobPath] = PdfBytes;
        await using var db = CreateDbContext();
        db.CapturedDocuments.Add(new CapturedDocumentEntity
        {
            Id = id,
            ClientDocumentId = Guid.NewGuid(),
            DocumentType = documentType,
            State = state,
            BlobPath = blobPath,
            PageCount = 1,
            SizeBytes = PdfBytes.Length,
            Sha256 = new string('a', 64),
            MatchedReference = matchedReference,
            MatchedCustomerName = matchedReference is null ? null : "Acme Stores (Pty) Ltd",
            MatchedBy = matchedReference is null ? null : "auto",
            AcumaticaAttachPending = attachPending,
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

    private static Extracted Fields(string json) => new(JsonDocument.Parse(json).RootElement.Clone());

    private PvmDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PvmDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new PvmDbContext(options);
    }

    private sealed class FakeExtractor : IDocumentExtractor
    {
        public ExtractionResult Result { get; set; } = new Unreadable("empty");

        public int Calls { get; private set; }

        public Func<Task>? OnExtract { get; set; }

        public async Task<ExtractionResult> ExtractAsync(IDocumentType type, Stream pdf, CancellationToken cancellationToken)
        {
            Calls++;
            if (OnExtract is not null)
            {
                await OnExtract();
            }

            using var buffer = new MemoryStream();
            await pdf.CopyToAsync(buffer, cancellationToken);
            Assert.Equal(PdfBytes, buffer.ToArray());
            return Result;
        }
    }

    private sealed class FakeAcumatica : IAcumaticaDocumentClient
    {
        public IReadOnlyList<CandidateHit> Hits { get; set; } = [];

        public Exception? FindException { get; set; }

        public int AttachFailures { get; set; }

        public HashSet<string> FailFor { get; } = new(StringComparer.Ordinal);

        public int FindCalls { get; private set; }

        public Func<Task>? OnFind { get; set; }

        public List<PodExtraction> Extractions { get; } = [];

        public List<string> AttachAttempts { get; } = [];

        public List<(string Entity, string ReferenceNumber, string FileName, byte[] Bytes)> Attached { get; } = [];

        public async Task<IReadOnlyList<CandidateHit>> FindInvoicesAsync(PodExtraction extraction, CancellationToken cancellationToken)
        {
            FindCalls++;
            Extractions.Add(extraction);
            if (OnFind is not null)
            {
                await OnFind();
            }

            if (FindException is not null)
            {
                throw FindException;
            }

            return Hits;
        }

        public Task<IReadOnlyList<InvoiceCandidate>> SearchInvoicesAsync(string query, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<InvoiceCandidate>>([]);

        public async Task AttachFileAsync(string entity, string referenceNumber, string fileName, Stream pdf, CancellationToken cancellationToken)
        {
            AttachAttempts.Add(referenceNumber);
            if (AttachFailures > 0)
            {
                AttachFailures--;
                throw new HttpRequestException("Acumatica refused the file.");
            }

            if (FailFor.Contains(referenceNumber))
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
