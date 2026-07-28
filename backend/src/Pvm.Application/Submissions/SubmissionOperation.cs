namespace Pvm.Application.Submissions;

public enum SubmissionOperationState
{
    Pending,
    Sending,
    Submitted,
    Rejected,
    Ambiguous
}

public sealed record SubmissionOperation(
    Guid Id,
    Guid InvoiceCandidateId,
    Guid CommandId,
    int Generation,
    SubmissionOperationState State,
    string SourceVersion,
    string? FrozenSourceJson,
    string? FrozenCanonicalJson,
    string? RequestPayload,
    string RequestPayloadHash,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PayloadArchiveRecord> PayloadArchives);

public sealed record PrepareSubmissionOperation(
    Guid InvoiceCandidateId,
    Guid CommandId,
    string InitiatedBy,
    string InitiationMode,
    string IdempotencyKey,
    string SourceVersion,
    string? FrozenSourceJson,
    string FrozenCanonicalJson,
    string RequestPayload);
