namespace Pvm.Application.Automation;

public enum AutomationMode
{
    Disabled,
    Shadow,
    Allowlisted,
    Enabled
}

public sealed record AutomationPolicy(
    int Version,
    AutomationMode Mode,
    bool EmergencyStop,
    IReadOnlyList<string> AccountAllowlist,
    IReadOnlyList<string> LocationAllowlist,
    IReadOnlyList<string> SupportedOrderTypes,
    int StabilizationDelayMinutes,
    int PurchaseOrderFreshnessMinutes,
    int AcumaticaFreshnessMinutes,
    int DailyAutomaticSubmissionCap,
    TimeOnly AutomaticWindowStart,
    TimeOnly AutomaticWindowEnd,
    string TimeZoneId,
    string CreatedBy,
    string Reason,
    DateTimeOffset CreatedAt);

public sealed record AutomationSubmissionPermission(
    bool Allowed,
    int PolicyVersion,
    string Code,
    string Message);

public interface IAutomationSubmissionGate
{
    Task<AutomationSubmissionPermission> EvaluateSubmissionAsync(
        Guid invoiceCandidateId,
        string initiationMode,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
