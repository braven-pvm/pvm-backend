namespace Pvm.Infrastructure.Persistence.Entities;

public sealed class SubmissionOperationEntity
{
    public Guid Id { get; set; }
    public Guid InvoiceCandidateId { get; set; }
    public Guid CommandId { get; set; }
    public Guid CorrelationId { get; set; }
    public int Generation { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string Status { get; set; }
    public required string InitiatedBy { get; set; }
    public required string InitiationMode { get; set; }
    public required string SourceVersion { get; set; }
    public string? FrozenSourceJson { get; set; }
    public string? FrozenCanonicalJson { get; set; }
    public string? RequestPayload { get; set; }
    public required string RequestPayloadHash { get; set; }
    public int? ResponseStatusCode { get; set; }
    public string? ResponsePayload { get; set; }
    public string? ResponsePayloadHash { get; set; }
    public string? ErrorMessage { get; set; }
    public string? FailureClassification { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SendingStartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public ICollection<PayloadArchiveEntity> PayloadArchives { get; set; } = [];
}
