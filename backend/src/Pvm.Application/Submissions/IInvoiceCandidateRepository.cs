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
        CancellationToken cancellationToken);

    Task<SubmissionOperation?> GetSubmissionOperationAsync(
        Guid submissionOperationId,
        CancellationToken cancellationToken);

    Task CompleteSubmissionOperationAsync(
        Guid submissionOperationId,
        ShopriteInvoiceResponse response,
        CancellationToken cancellationToken);

    Task<int> MarkStaleSendingOperationsAmbiguousAsync(
        DateTimeOffset staleBefore,
        DateTimeOffset detectedAt,
        CancellationToken cancellationToken);
}
