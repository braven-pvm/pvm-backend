using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pvm.Application.Documents;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Documents;

/// <summary>
/// Reads, matches and files one captured document (design 6.4 to 6.6), and attaches the filed
/// documents that wait for Acumatica. A document is never filed on a guess: anything that is not
/// a safe match goes to the review list. Transport failures of the extractor and of the Acumatica
/// lookup propagate, so Service Bus retries the message; the last delivery marks the row Failed.
/// </summary>
/// <remarks>
/// Every write is a conditional update. A run only writes a row that is still Processing, so a
/// stale run never overwrites a person's resolution; it logs the lost race and stops.
/// </remarks>
public sealed class DocumentProcessor(
    PvmDbContext dbContext,
    IEnumerable<IDocumentType> documentTypes,
    IDocumentExtractor extractor,
    IAcumaticaDocumentClient acumatica,
    IDocumentStore store,
    IOptions<DocumentCaptureOptions> options,
    ILogger<DocumentProcessor> logger)
{
    /// <summary>The <see cref="CapturedDocumentEntity.MatchedBy"/> value for a match the worker made.</summary>
    public const string MatchedByWorker = "auto";

    public const int BackfillBatchSize = 20;

    /// <summary>The backfill leaves a document alone for this long after an attach attempt.</summary>
    public static readonly TimeSpan BackfillRetryDelay = TimeSpan.FromMinutes(60);

    private const string ProofOfDeliveryCode = "POD";

    private static readonly JsonSerializerOptions FieldOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions CandidateOptions = new(JsonSerializerDefaults.Web);

    private readonly AcumaticaAttachMode _attachMode = options.Value.AcumaticaAttachMode;

    public async Task ProcessAsync(Guid documentId, CancellationToken cancellationToken)
    {
        // Received, or Processing after a crash mid-run: claim it. Filed, NeedsReview, Failed or
        // a missing row: nothing to do (a second delivery of the same message).
        var claimed = await dbContext.CapturedDocuments
            .Where(item => item.Id == documentId
                && (item.State == DocumentState.Received || item.State == DocumentState.Processing))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.State, DocumentState.Processing)
                    .SetProperty(item => item.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken);
        if (claimed == 0)
        {
            logger.LogInformation(
                "document.process.skipped DocumentId={DocumentId} Reason=not-received-or-processing",
                documentId);
            return;
        }

        var document = await dbContext.CapturedDocuments
            .AsNoTracking()
            .SingleAsync(item => item.Id == documentId, cancellationToken);

        var type = ResolveType(document.DocumentType);
        if (type is null || !string.Equals(type.Code, ProofOfDeliveryCode, StringComparison.OrdinalIgnoreCase))
        {
            // Only POD has a match strategy. Another type can never be filed automatically.
            await ReviewAsync(documentId, ReviewReason.ReadFailure, UnreadableJson("unknown-document-type"), cancellationToken);
            return;
        }

        ExtractionResult result;
        await using (var pdf = await store.OpenReadAsync(document.BlobPath, cancellationToken))
        {
            result = await extractor.ExtractAsync(type, pdf, cancellationToken);
        }

        if (result is not Extracted extracted)
        {
            var reason = result is Unreadable unreadable ? unreadable.Reason : "unknown-result";
            await ReviewAsync(documentId, ReviewReason.ReadFailure, UnreadableJson(reason), cancellationToken);
            return;
        }

        var fieldsJson = extracted.Fields.GetRawText();
        if (!await WhileProcessingAsync(
                documentId,
                setters => setters.SetProperty(item => item.ExtractionJson, fieldsJson),
                cancellationToken))
        {
            return;
        }

        var extraction = TryReadPodExtraction(extracted.Fields);
        if (extraction is null)
        {
            await ReviewAsync(documentId, ReviewReason.ReadFailure, fieldsJson, cancellationToken);
            return;
        }

        var hits = await acumatica.FindInvoicesAsync(extraction, cancellationToken);
        switch (PodMatcher.Decide(extraction, hits))
        {
            case Matched matched:
                await FileAsync(document, type, matched.Invoice, cancellationToken);
                break;

            case Review review:
                await ReviewAsync(
                    documentId,
                    review.Reason,
                    WithCandidates(extracted.Fields, review.Candidates),
                    cancellationToken);
                break;

            default:
                throw new InvalidOperationException("Unknown match outcome.");
        }
    }

    /// <summary>
    /// The dead-letter hook: the message for this document will not be delivered again, so a row
    /// that is still Received or Processing becomes Failed. The PDF and the row stay for a retry.
    /// </summary>
    public async Task MarkFailedAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var updated = await dbContext.CapturedDocuments
            .Where(item => item.Id == documentId
                && (item.State == DocumentState.Received || item.State == DocumentState.Processing))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.State, DocumentState.Failed)
                    .SetProperty(item => item.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken);
        logger.LogWarning(
            "document.process.failed DocumentId={DocumentId} RowsUpdated={RowsUpdated}",
            documentId,
            updated);
    }

    /// <summary>
    /// In Write mode, attaches up to <see cref="BackfillBatchSize"/> filed documents whose
    /// attachment waits: never-tried ones first, then the longest-waiting, oldest upload first.
    /// A document tried in the last <see cref="BackfillRetryDelay"/> is left for a later run, so
    /// documents that keep failing cannot block the rest. A failure is logged and the next
    /// document is tried. Shadow mode does nothing. Returns the number attached.
    /// </summary>
    public async Task<int> BackfillPendingAttachmentsAsync(CancellationToken cancellationToken)
    {
        if (_attachMode != AcumaticaAttachMode.Write)
        {
            return 0;
        }

        var retryBefore = DateTimeOffset.UtcNow - BackfillRetryDelay;
        var pending = await dbContext.CapturedDocuments
            .AsNoTracking()
            .Where(item => item.State == DocumentState.Filed
                && item.AcumaticaAttachPending
                && item.MatchedReference != null
                && (item.AcumaticaAttachLastAttemptAt == null || item.AcumaticaAttachLastAttemptAt < retryBefore))
            .OrderBy(item => item.AcumaticaAttachLastAttemptAt != null)
            .ThenBy(item => item.AcumaticaAttachLastAttemptAt)
            .ThenBy(item => item.UploadedAt)
            .ThenBy(item => item.Id)
            .Take(BackfillBatchSize)
            .ToListAsync(cancellationToken);

        var attached = 0;
        foreach (var document in pending)
        {
            var type = ResolveType(document.DocumentType);
            if (type is null)
            {
                await RecordAttachAttemptAsync(document.Id, cancellationToken);
                logger.LogWarning(
                    "document.attach.skipped DocumentId={DocumentId} Reason=unknown-document-type DocumentType={DocumentType}",
                    document.Id,
                    document.DocumentType);
                continue;
            }

            if (await TryAttachAsync(document, document.MatchedReference!, type, cancellationToken))
            {
                attached++;
            }
        }

        logger.LogInformation(
            "document.attach.backfill Pending={Pending} Attached={Attached}",
            pending.Count,
            attached);
        return attached;
    }

    /// <summary>The Acumatica file name: <c>{documentType}-{invoiceNumber}-{documentId}.pdf</c>.</summary>
    public static string AttachmentFileName(string documentType, string invoiceNumber, Guid documentId)
        => $"{documentType}-{invoiceNumber}-{documentId:D}.pdf";

    private async Task FileAsync(
        CapturedDocumentEntity document,
        IDocumentType type,
        InvoiceCandidate invoice,
        CancellationToken cancellationToken)
    {
        // Filed first, with the attachment pending: the recall works even if the attach fails.
        var filed = await WhileProcessingAsync(
            document.Id,
            setters => setters
                .SetProperty(item => item.State, DocumentState.Filed)
                .SetProperty(item => item.ReviewReason, (ReviewReason?)null)
                .SetProperty(item => item.MatchedReference, invoice.ReferenceNumber)
                .SetProperty(item => item.MatchedCustomerName, invoice.CustomerName)
                .SetProperty(item => item.MatchedBy, MatchedByWorker)
                .SetProperty(item => item.AcumaticaAttachPending, true)
                .SetProperty(item => item.AcumaticaAttachedAt, (DateTimeOffset?)null),
            cancellationToken);
        if (!filed)
        {
            return;
        }

        await AttachOrRecordAsync(document, type, invoice.ReferenceNumber, cancellationToken);
    }

    /// <summary>
    /// For a document a person just filed to <paramref name="reference"/>: in Write mode attaches
    /// it now (a failure is logged and the document stays pending for the backfill); in Shadow
    /// mode logs the attachment it would make. Returns true only when the file reached Acumatica.
    /// </summary>
    public async Task<bool> AttachFiledAsync(Guid documentId, string reference, CancellationToken cancellationToken)
    {
        var document = await dbContext.CapturedDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == documentId, cancellationToken);
        var type = document is null ? null : ResolveType(document.DocumentType);
        if (document is null || type is null)
        {
            logger.LogWarning(
                "document.attach.skipped DocumentId={DocumentId} Reason=missing-row-or-unknown-document-type",
                documentId);
            return false;
        }

        return await AttachOrRecordAsync(document, type, reference, cancellationToken);
    }

    private async Task<bool> AttachOrRecordAsync(
        CapturedDocumentEntity document,
        IDocumentType type,
        string reference,
        CancellationToken cancellationToken)
    {
        if (_attachMode == AcumaticaAttachMode.Write)
        {
            return await TryAttachAsync(document, reference, type, cancellationToken);
        }

        logger.LogInformation(
            "document.attach.shadow DocumentId={DocumentId} Entity={Entity} Reference={Reference} FileName={FileName}",
            document.Id,
            type.AcumaticaEntity,
            reference,
            AttachmentFileName(document.DocumentType, reference, document.Id));
        return false;
    }

    private async Task<bool> TryAttachAsync(
        CapturedDocumentEntity document,
        string reference,
        IDocumentType type,
        CancellationToken cancellationToken)
    {
        var fileName = AttachmentFileName(document.DocumentType, reference, document.Id);
        await RecordAttachAttemptAsync(document.Id, cancellationToken);
        try
        {
            await using var pdf = await store.OpenReadAsync(document.BlobPath, cancellationToken);
            await acumatica.AttachFileAsync(type.AcumaticaEntity, reference, fileName, pdf, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The document stays Filed with the attachment pending; the backfill tries again.
            logger.LogWarning(
                exception,
                "document.attach.failed DocumentId={DocumentId} Reference={Reference} FileName={FileName}",
                document.Id,
                reference,
                fileName);
            return false;
        }

        // Only clear the flag while the row is still filed to the invoice that got the file.
        var now = DateTimeOffset.UtcNow;
        var updated = await dbContext.CapturedDocuments
            .Where(item => item.Id == document.Id
                && item.State == DocumentState.Filed
                && item.MatchedReference == reference
                && item.AcumaticaAttachPending)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.AcumaticaAttachPending, false)
                    .SetProperty(item => item.AcumaticaAttachedAt, now)
                    .SetProperty(item => item.UpdatedAt, now),
                cancellationToken);
        logger.LogInformation(
            "document.attach.completed DocumentId={DocumentId} Reference={Reference} FileName={FileName} RowsUpdated={RowsUpdated}",
            document.Id,
            reference,
            fileName,
            updated);
        return true;
    }

    private Task<int> RecordAttachAttemptAsync(Guid documentId, CancellationToken cancellationToken)
        => dbContext.CapturedDocuments
            .Where(item => item.Id == documentId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.AcumaticaAttachLastAttemptAt, DateTimeOffset.UtcNow),
                cancellationToken);

    private async Task ReviewAsync(
        Guid documentId,
        ReviewReason reason,
        string extractionJson,
        CancellationToken cancellationToken)
    {
        if (await WhileProcessingAsync(
                documentId,
                setters => setters
                    .SetProperty(item => item.State, DocumentState.NeedsReview)
                    .SetProperty(item => item.ReviewReason, reason)
                    .SetProperty(item => item.ExtractionJson, extractionJson),
                cancellationToken))
        {
            logger.LogInformation(
                "document.process.review DocumentId={DocumentId} Reason={Reason}",
                documentId,
                reason);
        }
    }

    /// <summary>Updates the row only while it is still Processing; false (and a log) when another party moved it.</summary>
    private async Task<bool> WhileProcessingAsync(
        Guid documentId,
        Action<UpdateSettersBuilder<CapturedDocumentEntity>> set,
        CancellationToken cancellationToken)
    {
        var updated = await dbContext.CapturedDocuments
            .Where(item => item.Id == documentId && item.State == DocumentState.Processing)
            .ExecuteUpdateAsync(
                setters =>
                {
                    set(setters);
                    setters.SetProperty(item => item.UpdatedAt, DateTimeOffset.UtcNow);
                },
                cancellationToken);
        if (updated == 0)
        {
            logger.LogWarning(
                "document.process.superseded DocumentId={DocumentId} Reason=no-longer-processing",
                documentId);
        }

        return updated > 0;
    }

    private IDocumentType? ResolveType(string code)
        => documentTypes.FirstOrDefault(type => string.Equals(type.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>The extracted fields as a <see cref="PodExtraction"/>, or null when they do not fit its shape.</summary>
    private static PodExtraction? TryReadPodExtraction(JsonElement fields)
    {
        PodFields? parsed;
        try
        {
            parsed = fields.Deserialize<PodFields>(FieldOptions);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or FormatException)
        {
            return null;
        }

        if (parsed is null)
        {
            return null;
        }

        return new PodExtraction(
            Clean(parsed.InvoiceNumbers),
            Clean(parsed.SalesOrderNumbers),
            Clean(parsed.CustomerOrderNumbers),
            string.IsNullOrWhiteSpace(parsed.ClientName) ? null : parsed.ClientName.Trim(),
            parsed.DeliveryDate,
            parsed.SignaturePresent ?? false,
            parsed.Note);
    }

    private static IReadOnlyList<string> Clean(IReadOnlyList<string?>? values)
        => values is null
            ? []
            : values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .ToList();

    private static string UnreadableJson(string reason)
        => new JsonObject { ["unreadable"] = reason }.ToJsonString();

    /// <summary>The raw fields with the matcher's candidates next to them, for the review list.</summary>
    private static string WithCandidates(JsonElement fields, IReadOnlyList<InvoiceCandidate> candidates)
    {
        var json = JsonNode.Parse(fields.GetRawText())!.AsObject();
        json["candidates"] = JsonSerializer.SerializeToNode(candidates, CandidateOptions);
        return json.ToJsonString();
    }

    private sealed record PodFields(
        IReadOnlyList<string?>? InvoiceNumbers,
        IReadOnlyList<string?>? SalesOrderNumbers,
        IReadOnlyList<string?>? CustomerOrderNumbers,
        string? ClientName,
        DateOnly? DeliveryDate,
        bool? SignaturePresent,
        string? Note);
}
