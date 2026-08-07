namespace Pvm.Infrastructure.Persistence.Entities;

public sealed class IntegrationEventInboxEntity
{
    public Guid Id { get; set; }
    public required string SourceEnvironment { get; set; }
    public required string CompanyId { get; set; }
    public required string QueryName { get; set; }
    public Guid TransactionId { get; set; }
    public long NotificationTimestamp { get; set; }
    public required string PayloadJson { get; set; }
    public required string PayloadHash { get; set; }
    public int InsertedCount { get; set; }
    public int DeletedCount { get; set; }
    public int EnqueuedCount { get; set; }
    public int DuplicateCount { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset LastReceivedAt { get; set; }
}
