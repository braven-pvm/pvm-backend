namespace Pvm.Application.Submissions;

public interface IInvoiceCandidateRepository
{
    Task<InvoiceSubmissionSnapshot?> GetSubmissionSnapshotAsync(
        Guid invoiceCandidateId,
        CancellationToken cancellationToken);

    Task<SubmissionOperation> GetOrCreateSubmissionOperationAsync(
        PrepareSubmissionOperation request,
        CancellationToken cancellationToken);

    Task<bool> TryStartSubmissionOperationAsync(
        Guid submissionOperationId,
        DateTimeOffset startedAt,
        int? expectedAutomationPolicyVersion,
        bool automatic,
        CancellationToken cancellationToken);

    Task CancelPendingSubmissionOperationAsync(
        Guid submissionOperationId,
        string reason,
        DateTimeOffset cancelledAt,
        CancellationToken cancellationToken);

    Task<SubmissionOperation?> GetSubmissionOperationAsync(
        Guid submissionOperationId,
        CancellationToken cancellationToken);

    Task RecordPreparedPayloadArchivesAsync(
        Guid submissionOperationId,
        IReadOnlyCollection<PayloadArchiveRecord> payloads,
        CancellationToken cancellationToken);

    Task CompleteSubmissionOperationAsync(
        Guid submissionOperationId,
        ShopriteInvoiceResponse response,
        PayloadArchiveRecord responsePayload,
        CancellationToken cancellationToken);

    Task MarkSubmissionOperationArchiveFailureAmbiguousAsync(
        Guid submissionOperationId,
        DateTimeOffset detectedAt,
        CancellationToken cancellationToken);

    Task<int> MarkStaleSendingOperationsAmbiguousAsync(
        DateTimeOffset staleBefore,
        DateTimeOffset detectedAt,
        CancellationToken cancellationToken);
}
