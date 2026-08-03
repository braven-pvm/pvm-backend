namespace Pvm.Infrastructure.Persistence.Entities;

public sealed class IntegrationMessageDeliveryEntity
{
    public Guid Id { get; set; }
    public required string QueueName { get; set; }
    public required string MessageId { get; set; }
    public required string MessageType { get; set; }
    public required string CorrelationId { get; set; }
    public string? CausationId { get; set; }
    public string Status { get; set; } = "Received";
    public int DeliveryCount { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorSummary { get; set; }
    public string? DeadLetterReason { get; set; }
    public DateTimeOffset EnqueuedAt { get; set; }
    public DateTimeOffset FirstReceivedAt { get; set; }
    public DateTimeOffset LastReceivedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
