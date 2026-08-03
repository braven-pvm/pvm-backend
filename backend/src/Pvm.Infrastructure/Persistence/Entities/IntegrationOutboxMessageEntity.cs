namespace Pvm.Infrastructure.Persistence.Entities;

public sealed class IntegrationOutboxMessageEntity
{
    public Guid Id { get; set; }
    public required string QueueName { get; set; }
    public required string MessageType { get; set; }
    public required string PayloadJson { get; set; }
    public required string CorrelationId { get; set; }
    public string? CausationId { get; set; }
    public string Status { get; set; } = "Pending";
    public int PublishAttempts { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorSummary { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}
