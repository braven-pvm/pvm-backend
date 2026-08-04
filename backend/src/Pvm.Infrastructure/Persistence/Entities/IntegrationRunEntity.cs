namespace Pvm.Infrastructure.Persistence.Entities;

public sealed class IntegrationRunEntity
{
    public Guid Id { get; set; }
    public required string RunType { get; set; }
    public required string Trigger { get; set; }
    public required string InitiatedBy { get; set; }
    public required string EnvironmentName { get; set; }
    public required string CorrelationId { get; set; }
    public Guid? MessageId { get; set; }
    public string? ScheduleKey { get; set; }
    public required string Status { get; set; }
    public int AttemptCount { get; set; }
    public int ReceivedCount { get; set; }
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int UnchangedCount { get; set; }
    public int SkippedCount { get; set; }
    public int RevalidatedCount { get; set; }
    public int FailedCount { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorSummary { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
