namespace Pvm.Infrastructure.Persistence.Entities;

public sealed class ExceptionTaskCommentEntity
{
    public Guid Id { get; set; }
    public Guid ExceptionTaskId { get; set; }
    public required string Actor { get; set; }
    public required string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
