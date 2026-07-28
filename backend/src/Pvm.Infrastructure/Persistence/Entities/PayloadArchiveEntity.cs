namespace Pvm.Infrastructure.Persistence.Entities;

public sealed class PayloadArchiveEntity
{
    public Guid Id { get; set; }
    public Guid SubmissionOperationId { get; set; }
    public Guid InvoiceCandidateId { get; set; }
    public required string Kind { get; set; }
    public required string Location { get; set; }
    public required string Sha256Hash { get; set; }
    public required string ContentType { get; set; }
    public long ByteCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
