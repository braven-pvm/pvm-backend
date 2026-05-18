namespace Pvm.Infrastructure.Persistence.Entities;

public sealed class AuditEventEntity
{
    public Guid Id { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public required string Action { get; set; }
    public required string Actor { get; set; }
    public string? DetailsJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
