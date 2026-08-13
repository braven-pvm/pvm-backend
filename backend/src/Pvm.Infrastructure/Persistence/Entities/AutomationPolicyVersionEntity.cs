namespace Pvm.Infrastructure.Persistence.Entities;

public sealed class AutomationPolicyVersionEntity
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public required string Mode { get; set; }
    public bool EmergencyStop { get; set; }
    public string[] AccountAllowlist { get; set; } = [];
    public string[] LocationAllowlist { get; set; } = [];
    public string[] SupportedOrderTypes { get; set; } = [];
    public int StabilizationDelayMinutes { get; set; }
    public int PurchaseOrderFreshnessMinutes { get; set; }
    public int AcumaticaFreshnessMinutes { get; set; }
    public int DailyAutomaticSubmissionCap { get; set; }
    public TimeOnly AutomaticWindowStart { get; set; }
    public TimeOnly AutomaticWindowEnd { get; set; }
    public required string TimeZoneId { get; set; }
    public required string CreatedBy { get; set; }
    public required string Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
