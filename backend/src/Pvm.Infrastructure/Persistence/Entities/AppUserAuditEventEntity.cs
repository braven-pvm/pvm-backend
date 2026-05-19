namespace Pvm.Infrastructure.Persistence.Entities;

public sealed class AppUserAuditEventEntity
{
    public Guid Id { get; set; }
    public Guid? ActorAppUserId { get; set; }
    public Guid TargetAppUserId { get; set; }
    public required string Action { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
