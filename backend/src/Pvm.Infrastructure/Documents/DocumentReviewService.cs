using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pvm.Application.Documents;
using Pvm.Infrastructure.Persistence;

namespace Pvm.Infrastructure.Documents;

public enum FileToOutcome
{
    Filed,
    NotFound,

    /// <summary>The document is not in NeedsReview or Failed (any more).</summary>
    NotOpen,

    /// <summary>The Acumatica search has no invoice with exactly that reference number.</summary>
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

/// <summary>
/// A person's resolution of a document on the review list (design 6.8): file it to an invoice
/// that the Acumatica search confirms, or reject it with a note. Both writes are conditional on
/// the row still being open, so they never race the processor or another reviewer.
/// </summary>
public sealed class DocumentReviewService(
    PvmDbContext dbContext,
    IAcumaticaDocumentClient acumatica,
    DocumentProcessor processor,
    ILogger<DocumentReviewService> logger)
{
    /// <summary>A document still Received or Processing this long after the upload shows on the review list as stuck.</summary>
    public static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(30);

    public const int MaxNoteLength = 1000;

    public static string MatchedByUser(string email) => $"user:{email}";

    public async Task<FileToResult> FileToAsync(
        Guid documentId,
        string invoiceReference,
        string user,
        CancellationToken cancellationToken)
    {
        var state = await dbContext.CapturedDocuments
            .Where(item => item.Id == documentId)
            .Select(item => (DocumentState?)item.State)
            .SingleOrDefaultAsync(cancellationToken);
        if (state is null)
        {
            return new FileToResult(FileToOutcome.NotFound);
        }

        if (state is not (DocumentState.NeedsReview or DocumentState.Failed))
        {
            return new FileToResult(FileToOutcome.NotOpen);
        }

        // Only an exact reference counts: the search also returns prefix and customer-name hits,
        // and a document filed to the wrong invoice is the outcome this feature must not produce.
        var results = await acumatica.SearchInvoicesAsync(invoiceReference, cancellationToken);
        var invoice = results.FirstOrDefault(result =>
            string.Equals(result.ReferenceNumber, invoiceReference, StringComparison.OrdinalIgnoreCase));
        if (invoice is null)
        {
            return new FileToResult(FileToOutcome.InvoiceNotFound);
        }

        var now = DateTimeOffset.UtcNow;
        var matchedBy = MatchedByUser(user);
        var updated = await dbContext.CapturedDocuments
            .Where(item => item.Id == documentId
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
            .Select(item => new { item.State, item.RejectedAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (current is null)
        {
            return RejectOutcome.NotFound;
        }

        if (current.State is not (DocumentState.NeedsReview or DocumentState.Failed) || current.RejectedAt is not null)
        {
            return RejectOutcome.NotOpen;
        }

        var now = DateTimeOffset.UtcNow;
        var updated = await dbContext.CapturedDocuments
            .Where(item => item.Id == documentId
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
            .Select(item => new { item.State, item.AcumaticaAttachedAt })
            .SingleOrDefaultAsync(cancellationToken);
        return current switch
        {
            null => ReturnToReviewOutcome.NotFound,
            { State: not DocumentState.Filed } => ReturnToReviewOutcome.NotFiled,
            { AcumaticaAttachedAt: not null } => ReturnToReviewOutcome.AlreadyAttached,
            _ => ReturnToReviewOutcome.Returned,
        };
    }
}
