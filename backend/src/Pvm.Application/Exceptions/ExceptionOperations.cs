namespace Pvm.Application.Exceptions;

public static class ExceptionCategories
{
    public const string AmbiguousSubmission = "AmbiguousSubmission";
    public const string RejectedSubmission = "RejectedSubmission";
    public const string NeedsReview = "NeedsReview";
    public const string DeadLetter = "DeadLetter";
    public const string StuckWork = "StuckWork";
    public const string HeldInvoice = "HeldInvoice";
}

public static class ExceptionSeverities
{
    public const string Critical = "Critical";
    public const string High = "High";
    public const string Medium = "Medium";
    public const string Low = "Low";
}

public static class ExceptionTaskStatuses
{
    public const string Open = "Open";
    public const string InProgress = "InProgress";
    public const string WaitingForAcumatica = "WaitingForAcumatica";
    public const string WaitingForShoprite = "WaitingForShoprite";
    public const string Suppressed = "Suppressed";
    public const string Resolved = "Resolved";

    public static readonly string[] All =
    [
        Open,
        InProgress,
        WaitingForAcumatica,
        WaitingForShoprite,
        Suppressed,
        Resolved
    ];

    public static bool IsActive(string status)
        => status is Open or InProgress or WaitingForAcumatica or WaitingForShoprite;
}

public static class ExceptionRetryClassifications
{
    public const string ManualResolutionRequired = "manual-resolution-required";
    public const string RetryAfterFix = "retry-after-fix";
    public const string SafeToReplay = "safe-to-replay";
    public const string NoRetry = "no-retry";
}

public enum AmbiguousResolutionOutcome
{
    ConfirmedAccepted,
    ConfirmedNotAccepted,
    StillUnknown
}

public sealed record ExceptionTaskComment(
    Guid Id,
    string Actor,
    string Body,
    DateTimeOffset CreatedAt);

public sealed record ExceptionTaskView(
    Guid Id,
    string DeduplicationKey,
    string Category,
    string Severity,
    string Status,
    string EntityType,
    string EntityId,
    string? InvoiceCandidateId,
    string? InvoiceNumber,
    string ErrorCode,
    string Summary,
    string FixLocation,
    string RetryClassification,
    string? Owner,
    int OccurrenceCount,
    string? LatestEvidence,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? DueAt,
    bool IsOverdue,
    DateTimeOffset? ResolvedAt,
    string? ResolvedBy,
    string? ResolutionReason,
    IReadOnlyList<ExceptionTaskComment> Comments);

public sealed record ExceptionQueueSummary(
    int Ambiguous,
    int Rejected,
    int NeedsReview,
    int DeadLetters,
    int Stuck,
    int Held,
    int Overdue,
    int Resolved);

public sealed record ExceptionQueueListing(
    IReadOnlyList<ExceptionTaskView> Tasks,
    ExceptionQueueSummary Summary);

public sealed record ExceptionOperationResult(
    bool Applied,
    string? Error = null,
    bool Conflict = false,
    ExceptionTaskView? Task = null,
    string? Detail = null);

public sealed record DeadLetterReplaySafety(
    bool Safe,
    string Reason,
    string? BlockingState = null);
