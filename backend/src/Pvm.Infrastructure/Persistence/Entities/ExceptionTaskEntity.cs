namespace Pvm.Infrastructure.Persistence.Entities;

public sealed class ExceptionTaskEntity
{
    public Guid Id { get; set; }
    public required string DeduplicationKey { get; set; }
    public required string Category { get; set; }
    public required string Severity { get; set; }
    public required string Status { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public Guid? InvoiceCandidateId { get; set; }
    public required string ErrorCode { get; set; }
    public required string Summary { get; set; }
    public required string FixLocation { get; set; }
    public required string RetryClassification { get; set; }
    public string? Owner { get; set; }
    public int OccurrenceCount { get; set; }
    public string? LatestEvidence { get; set; }
    public string? DetailsJson { get; set; }
    public bool IsDerived { get; set; } = true;
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }
    public string? ResolutionReason { get; set; }
}
