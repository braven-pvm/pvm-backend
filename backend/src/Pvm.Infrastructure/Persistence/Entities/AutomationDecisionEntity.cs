namespace Pvm.Infrastructure.Persistence.Entities;

public sealed class AutomationDecisionEntity
{
    public Guid Id { get; set; }
    public Guid InvoiceCandidateId { get; set; }
    public int PolicyVersion { get; set; }
    public required string SourceVersion { get; set; }
    public required string Outcome { get; set; }
    public string[] ReasonCodes { get; set; } = [];
    public required string Summary { get; set; }
    public DateTimeOffset? NotBefore { get; set; }
    public Guid? CommandId { get; set; }
    public Guid? MessageId { get; set; }
    public DateTimeOffset EvaluatedAt { get; set; }
}
