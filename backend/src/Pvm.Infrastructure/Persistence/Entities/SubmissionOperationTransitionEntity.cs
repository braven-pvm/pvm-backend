namespace Pvm.Infrastructure.Persistence.Entities;

public sealed class SubmissionOperationTransitionEntity
{
    public Guid Id { get; set; }
    public Guid SubmissionOperationId { get; set; }
    public Guid InvoiceCandidateId { get; set; }
    public Guid CorrelationId { get; set; }
    public required string Actor { get; set; }
    public required string Mode { get; set; }
    public string? PreviousState { get; set; }
    public required string NewState { get; set; }
    public required string Reason { get; set; }
    public required string SourceVersion { get; set; }
    public required string PayloadHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
