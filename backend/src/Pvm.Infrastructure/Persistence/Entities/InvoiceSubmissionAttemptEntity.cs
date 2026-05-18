namespace Pvm.Infrastructure.Persistence.Entities;

public sealed class InvoiceSubmissionAttemptEntity
{
    public Guid Id { get; set; }
    public Guid InvoiceCandidateId { get; set; }
    public required string InitiatedBy { get; set; }
    public required string InitiationMode { get; set; }
    public required string Status { get; set; }
    public string? RequestPayloadLocation { get; set; }
    public string? RequestPayloadHash { get; set; }
    public int? ResponseStatusCode { get; set; }
    public string? ResponsePayloadLocation { get; set; }
    public string? ResponsePayloadHash { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
