using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pvm.Application.Documents;
using Pvm.Application.Messaging;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Documents;

public enum FileToOutcome
{
    Filed,
    NotFound,

    /// <summary>The document is not in NeedsReview or Failed (any more).</summary>
    NotOpen,

    /// <summary>Acumatica has no invoice with exactly that reference number that is not voided or cancelled.</summary>
    InvoiceNotFound,
}

public sealed record FileToResult(FileToOutcome Outcome, InvoiceCandidate? Invoice = null);

public enum RejectOutcome
{
    Rejected,
    NotFound,

    /// <summary>The document is not in NeedsReview or Failed, or it is already rejected.</summary>
    NotOpen,
}

public enum ReturnToReviewOutcome
{
    Returned,
    NotFound,

    /// <summary>The document is not Filed.</summary>
    NotFiled,

    /// <summary>The PDF already reached Acumatica; it must be removed there by hand.</summary>
    AlreadyAttached,
}

public enum ProcessAgainOutcome
{
    Queued,
    NotFound,

    /// <summary>The document is not Failed, nor Received or Processing for longer than <see cref="DocumentReviewService.StuckAfter"/>.</summary>
    NotRetryable,
}

public enum DeleteOutcome
{
    Deleted,
    NotFound,

    /// <summary>The document is Filed; send it back to review first.</summary>
    Filed,

    /// <summary>The PDF already reached Acumatica.</summary>
    Attached,

    /// <summary>Received or Processing, uploaded less than <see cref="DocumentReviewService.StuckAfter"/> ago.</summary>
    Processing,
}

/// <param name="BlobDeleteFailed">The row is gone but the PDF could not be removed from storage; it is logged with its path.</param>
public sealed record DeleteResult(DeleteOutcome Outcome, bool BlobDeleteFailed = false);

/// <summary>
/// A person's resolution of a document on the review list (design 6.8): file it to an invoice
/// that an exact Acumatica lookup confirms, or reject it with a note. Both writes are conditional on
/// the row still being open, so they never race the processor or another reviewer.
/// </summary>
public sealed class DocumentReviewService(
    PvmDbContext dbContext,
    IAcumaticaDocumentClient acumatica,
    DocumentProcessor processor,
    IIntegrationCommandQueue commandQueue,
    IDocumentStore documentStore,
    ILogger<DocumentReviewService> logger)
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>A document still Received or Processing this long after the upload shows on the review list as stuck.</summary>
    public static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(30);

    public const int MaxNoteLength = 1000;

    public const int MaxDeleteReasonLength = 500;

    public static string MatchedByUser(string email) => $"user:{email}";

    public async Task<FileToResult> FileToAsync(
        Guid documentId,
        string invoiceReference,
        string user,
        CancellationToken cancellationToken)
    {
        var current = await dbContext.CapturedDocuments
            .Where(item => item.Id == documentId)
            .Select(item => new { item.State, item.DocumentType })
            .SingleOrDefaultAsync(cancellationToken);
        if (current is null)
        {
            return new FileToResult(FileToOutcome.NotFound);
        }

        // A general document is never on the review list and never filed to an invoice (design 6.12).
        if (current.State is not (DocumentState.NeedsReview or DocumentState.Failed)
            || DocumentTypeCodes.IsGeneral(current.DocumentType))
        {
            return new FileToResult(FileToOutcome.NotOpen);
        }

        // Only an exact reference counts (an exact lookup, not the prefix search): a document filed
        // to the wrong invoice is the outcome this feature must not produce.
        var invoice = await acumatica.GetInvoiceAsync(invoiceReference, cancellationToken);
        if (invoice is null)
        {
            return new FileToResult(FileToOutcome.InvoiceNotFound);
        }

        var now = DateTimeOffset.UtcNow;
        var matchedBy = MatchedByUser(user);
        var updated = await dbContext.CapturedDocuments
            .Where(item => item.Id == documentId
                && item.DocumentType != DocumentTypeCodes.General
                && (item.State == DocumentState.NeedsReview || item.State == DocumentState.Failed))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.State, DocumentState.Filed)
                    .SetProperty(item => item.MatchedReference, invoice.ReferenceNumber)
                    .SetProperty(item => item.MatchedCustomerName, invoice.CustomerName)
                    .SetProperty(item => item.MatchedBy, matchedBy)
                    .SetProperty(item => item.ResolvedBy, user)
                    .SetProperty(item => item.ResolvedAt, now)
                    .SetProperty(item => item.ReviewNote, (string?)null)
                    .SetProperty(item => item.RejectedAt, (DateTimeOffset?)null)
                    .SetProperty(item => item.RejectedBy, (string?)null)
                    .SetProperty(item => item.AcumaticaAttachPending, true)
                    .SetProperty(item => item.AcumaticaAttachedAt, (DateTimeOffset?)null)
                    .SetProperty(item => item.AcumaticaAttachLastAttemptAt, (DateTimeOffset?)null)
                    .SetProperty(item => item.UpdatedAt, now),
                cancellationToken);
        if (updated == 0)
        {
            logger.LogWarning(
                "document.review.file-to.superseded DocumentId={DocumentId} Reason=no-longer-open",
                documentId);
            return new FileToResult(FileToOutcome.NotOpen);
        }

        logger.LogInformation(
            "document.review.filed DocumentId={DocumentId} Reference={Reference} MatchedBy={MatchedBy}",
            documentId,
            invoice.ReferenceNumber,
            matchedBy);

        try
        {
            await processor.AttachFiledAsync(documentId, invoice.ReferenceNumber, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The document is filed with the attachment pending; the backfill tries again.
            logger.LogWarning(
                exception,
                "document.review.attach-failed DocumentId={DocumentId} Reference={Reference}",
                documentId,
                invoice.ReferenceNumber);
        }

        return new FileToResult(FileToOutcome.Filed, invoice);
    }

    /// <summary>
    /// Takes the document off the open review list with a note, for example a bad scan. The
    /// state stays as it is (NeedsReview or Failed); <c>RejectedAt</c> is the marker.
    /// </summary>
    public async Task<RejectOutcome> RejectAsync(
        Guid documentId,
        string note,
        string user,
        CancellationToken cancellationToken)
    {
        var current = await dbContext.CapturedDocuments
            .Where(item => item.Id == documentId)
            .Select(item => new { item.State, item.RejectedAt, item.DocumentType })
            .SingleOrDefaultAsync(cancellationToken);
        if (current is null)
        {
            return RejectOutcome.NotFound;
        }

        if (current.State is not (DocumentState.NeedsReview or DocumentState.Failed)
            || current.RejectedAt is not null
            || DocumentTypeCodes.IsGeneral(current.DocumentType))
        {
            return RejectOutcome.NotOpen;
        }

        var now = DateTimeOffset.UtcNow;
        var updated = await dbContext.CapturedDocuments
            .Where(item => item.Id == documentId
                && item.DocumentType != DocumentTypeCodes.General
                && (item.State == DocumentState.NeedsReview || item.State == DocumentState.Failed)
                && item.RejectedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.ReviewNote, note)
                    .SetProperty(item => item.RejectedAt, now)
                    .SetProperty(item => item.RejectedBy, user)
                    .SetProperty(item => item.ResolvedBy, user)
                    .SetProperty(item => item.ResolvedAt, now)
                    .SetProperty(item => item.UpdatedAt, now),
                cancellationToken);
        if (updated == 0)
        {
            logger.LogWarning(
                "document.review.reject.superseded DocumentId={DocumentId} Reason=no-longer-open",
                documentId);
            return RejectOutcome.NotOpen;
        }

        logger.LogInformation("document.review.rejected DocumentId={DocumentId}", documentId);
        return RejectOutcome.Rejected;
    }

    /// <summary>
    /// Takes a wrongly filed document back to the review list with a note. Allowed only while the
    /// PDF has not reached Acumatica: the row is Filed and <c>AcumaticaAttachedAt</c> is null.
    /// The match is cleared and the attachment no longer waits, so the backfill leaves it alone.
    /// </summary>
    public async Task<ReturnToReviewOutcome> ReturnToReviewAsync(
        Guid documentId,
        string note,
        string user,
        CancellationToken cancellationToken)
    {
        var current = await CurrentForReturnAsync(documentId, cancellationToken);
        if (current is not ReturnToReviewOutcome.Returned)
        {
            return current;
        }

        var now = DateTimeOffset.UtcNow;
        var updated = await dbContext.CapturedDocuments
            .Where(item => item.Id == documentId
                && item.DocumentType != DocumentTypeCodes.General
                && item.State == DocumentState.Filed
                && item.AcumaticaAttachedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.State, DocumentState.NeedsReview)
                    .SetProperty(item => item.ReviewReason, (ReviewReason?)null)
                    .SetProperty(item => item.MatchedReference, (string?)null)
                    .SetProperty(item => item.MatchedCustomerName, (string?)null)
                    .SetProperty(item => item.MatchedBy, (string?)null)
                    .SetProperty(item => item.AcumaticaAttachPending, false)
                    .SetProperty(item => item.ReviewNote, note)
                    .SetProperty(item => item.RejectedAt, (DateTimeOffset?)null)
                    .SetProperty(item => item.RejectedBy, (string?)null)
                    .SetProperty(item => item.ResolvedBy, user)
                    .SetProperty(item => item.ResolvedAt, now)
                    .SetProperty(item => item.UpdatedAt, now),
                cancellationToken);
        if (updated == 0)
        {
            // The attach (or another person) won the race: say why from the row as it is now.
            var outcome = await CurrentForReturnAsync(documentId, cancellationToken);
            logger.LogWarning(
                "document.review.return.superseded DocumentId={DocumentId} Outcome={Outcome}",
                documentId,
                outcome);
            return outcome is ReturnToReviewOutcome.Returned ? ReturnToReviewOutcome.NotFiled : outcome;
        }

        logger.LogInformation(
            "document.review.returned DocumentId={DocumentId} ResolvedBy={ResolvedBy}",
            documentId,
            user);
        return ReturnToReviewOutcome.Returned;
    }

    /// <summary>What a return to review would answer for the row as it is now; Returned means it is allowed.</summary>
    private async Task<ReturnToReviewOutcome> CurrentForReturnAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var current = await dbContext.CapturedDocuments
            .AsNoTracking()
            .Where(item => item.Id == documentId)
            .Select(item => new { item.State, item.AcumaticaAttachedAt, item.DocumentType })
            .SingleOrDefaultAsync(cancellationToken);
        return current switch
        {
            null => ReturnToReviewOutcome.NotFound,
            { State: not DocumentState.Filed } => ReturnToReviewOutcome.NotFiled,
            // A general document is filed as stored, not to an invoice: there is no review to go back to.
            _ when DocumentTypeCodes.IsGeneral(current.DocumentType) => ReturnToReviewOutcome.NotFiled,
            { AcumaticaAttachedAt: not null } => ReturnToReviewOutcome.AlreadyAttached,
            _ => ReturnToReviewOutcome.Returned,
        };
    }

    /// <summary>
    /// Sends a Failed or stuck document through processing again: the row goes back to Received
    /// (the stored extraction stays) and a new <c>document.received.v1</c> message is written to
    /// the outbox in the same transaction.
    /// </summary>
    public async Task<ProcessAgainOutcome> ProcessAgainAsync(Guid documentId, string user, CancellationToken cancellationToken)
    {
        var stuckBefore = DateTimeOffset.UtcNow - StuckAfter;
        var current = await dbContext.CapturedDocuments
            .AsNoTracking()
            .Where(item => item.Id == documentId)
            .Select(item => new { item.State, item.UploadedAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (current is null)
        {
            return ProcessAgainOutcome.NotFound;
        }

        if (!IsRetryable(current.State, current.UploadedAt, stuckBefore))
        {
            return ProcessAgainOutcome.NotRetryable;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var updated = await dbContext.CapturedDocuments
            .Where(item => item.Id == documentId
                && (item.State == DocumentState.Failed
                    || ((item.State == DocumentState.Received || item.State == DocumentState.Processing)
                        && item.UploadedAt < stuckBefore)))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.State, DocumentState.Received)
                    .SetProperty(item => item.ReviewReason, (ReviewReason?)null)
                    .SetProperty(item => item.RejectedAt, (DateTimeOffset?)null)
                    .SetProperty(item => item.RejectedBy, (string?)null)
                    .SetProperty(item => item.UpdatedAt, now),
                cancellationToken);
        if (updated == 0)
        {
            logger.LogWarning(
                "document.review.process-again.superseded DocumentId={DocumentId} Reason=no-longer-failed-or-stuck",
                documentId);
            return ProcessAgainOutcome.NotRetryable;
        }

        var messageId = await commandQueue.EnqueueAsync(
            IntegrationQueues.DocumentProcessing,
            IntegrationMessageTypes.DocumentReceivedV1,
            new ProcessCapturedDocumentMessage(documentId),
            documentId.ToString("D"),
            cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "document.review.process-again DocumentId={DocumentId} PreviousState={PreviousState} MessageId={MessageId} RequestedBy={RequestedBy}",
            documentId,
            current.State,
            messageId,
            user);
        return ProcessAgainOutcome.Queued;
    }

    /// <summary>
    /// The delete rule (design 6.11), for one row as it is: <see cref="DeleteOutcome.Deleted"/> means
    /// a delete is allowed. Only an unfiled document whose PDF is not in Acumatica can go: NeedsReview,
    /// Failed, or Received/Processing once it is stuck. A general document is never attached, so a
    /// Filed one can go as well (design 6.12). The detail endpoint shows it as <c>canDelete</c>.
    /// </summary>
    public static DeleteOutcome DeleteCheck(
        string documentType,
        DocumentState state,
        DateTimeOffset? acumaticaAttachedAt,
        DateTimeOffset uploadedAt,
        DateTimeOffset stuckBefore)
    {
        if (acumaticaAttachedAt is not null)
        {
            return DeleteOutcome.Attached;
        }

        if (state == DocumentState.Filed && DocumentTypeCodes.IsGeneral(documentType))
        {
            return DeleteOutcome.Deleted;
        }

        return state switch
        {
            DocumentState.NeedsReview or DocumentState.Failed => DeleteOutcome.Deleted,
            DocumentState.Received or DocumentState.Processing => uploadedAt < stuckBefore ? DeleteOutcome.Deleted : DeleteOutcome.Processing,
            _ => DeleteOutcome.Filed,
        };
    }

    /// <summary>
    /// Removes an unfiled document for good (design 6.11): a conditional DELETE of the row and an
    /// audit event in one transaction, then the PDF. The DELETE repeats the rule in its WHERE, so a
    /// row that the worker or another person filed meanwhile is never deleted. If the PDF cannot be
    /// removed after the row is gone, the delete stands and the result says so; the path is logged.
    /// A later processing message for the document finds no row and completes quietly. A phone that
    /// uploads the same clientDocumentId again after the delete creates a new document; that is
    /// accepted, because the person chose to remove the old one.
    /// </summary>
    public async Task<DeleteResult> DeleteAsync(
        Guid documentId,
        string reason,
        string user,
        CancellationToken cancellationToken)
    {
        var stuckBefore = DateTimeOffset.UtcNow - StuckAfter;
        var document = await dbContext.CapturedDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == documentId, cancellationToken);
        if (document is null)
        {
            return new DeleteResult(DeleteOutcome.NotFound);
        }

        var check = DeleteCheck(document.DocumentType, document.State, document.AcumaticaAttachedAt, document.UploadedAt, stuckBefore);
        if (check is not DeleteOutcome.Deleted)
        {
            return new DeleteResult(check);
        }

        var deviceName = document.ScannerDeviceId is { } deviceId
            ? await dbContext.ScannerDevices
                .AsNoTracking()
                .Where(device => device.Id == deviceId)
                .Select(device => device.Name)
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        var now = DateTimeOffset.UtcNow;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            var deleted = await dbContext.CapturedDocuments
                .Where(item => item.Id == documentId
                    && item.AcumaticaAttachedAt == null
                    && (item.State == DocumentState.NeedsReview
                        || item.State == DocumentState.Failed
                        || (item.State == DocumentState.Filed && item.DocumentType == DocumentTypeCodes.General)
                        || ((item.State == DocumentState.Received || item.State == DocumentState.Processing)
                            && item.UploadedAt < stuckBefore)))
                .ExecuteDeleteAsync(cancellationToken);
            if (deleted == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                var outcome = await CurrentDeleteCheckAsync(documentId, stuckBefore, cancellationToken);
                logger.LogWarning(
                    "document.delete.superseded DocumentId={DocumentId} Outcome={Outcome}",
                    documentId,
                    outcome);
                // The row changed and changed back between the two reads: treat it as still busy.
                return new DeleteResult(outcome is DeleteOutcome.Deleted ? DeleteOutcome.Processing : outcome);
            }

            dbContext.AuditEvents.Add(new AuditEventEntity
            {
                Id = Guid.NewGuid(),
                EntityType = "CapturedDocument",
                EntityId = documentId.ToString("D"),
                Action = "document-deleted",
                Actor = user,
                DetailsJson = JsonSerializer.Serialize(
                    new
                    {
                        documentId,
                        reason,
                        deletedBy = user,
                        deletedAt = now,
                        documentType = document.DocumentType,
                        state = document.State.ToString(),
                        uploadedBy = document.UploadedBy,
                        uploadedAt = document.UploadedAt,
                        scannerDeviceId = document.ScannerDeviceId,
                        scannerDeviceName = deviceName,
                        scannerStaffId = document.ScannerStaffId,
                        scannerStaffName = document.ScannerStaffName,
                        clientDocumentId = document.ClientDocumentId,
                        sha256 = document.Sha256,
                        blobPath = document.BlobPath,
                        title = document.Title,
                        thumbnailBlobPath = document.ThumbnailBlobPath,
                    },
                    AuditJsonOptions),
                CreatedAt = now,
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        logger.LogInformation(
            "document.deleted DocumentId={DocumentId} State={State} DeletedBy={DeletedBy}",
            documentId,
            document.State,
            user);

        var blobDeleteFailed = false;
        foreach (var path in new[] { document.BlobPath, document.ThumbnailBlobPath })
        {
            if (path is null)
            {
                continue;
            }

            try
            {
                await documentStore.DeleteAsync(path, CancellationToken.None);
            }
            catch (Exception exception)
            {
                // The row and its audit event are gone for good; the file is left behind. Never roll back.
                logger.LogError(
                    exception,
                    "document.delete.blob-failed DocumentId={DocumentId} BlobPath={BlobPath}",
                    documentId,
                    path);
                blobDeleteFailed = true;
            }
        }

        return new DeleteResult(DeleteOutcome.Deleted, BlobDeleteFailed: blobDeleteFailed);
    }

    private async Task<DeleteOutcome> CurrentDeleteCheckAsync(
        Guid documentId,
        DateTimeOffset stuckBefore,
        CancellationToken cancellationToken)
    {
        var current = await dbContext.CapturedDocuments
            .AsNoTracking()
            .Where(item => item.Id == documentId)
            .Select(item => new { item.DocumentType, item.State, item.AcumaticaAttachedAt, item.UploadedAt })
            .SingleOrDefaultAsync(cancellationToken);
        return current is null
            ? DeleteOutcome.NotFound
            : DeleteCheck(current.DocumentType, current.State, current.AcumaticaAttachedAt, current.UploadedAt, stuckBefore);
    }

    private static bool IsRetryable(DocumentState state, DateTimeOffset uploadedAt, DateTimeOffset stuckBefore)
        => state == DocumentState.Failed
            || (state is DocumentState.Received or DocumentState.Processing && uploadedAt < stuckBefore);
}
