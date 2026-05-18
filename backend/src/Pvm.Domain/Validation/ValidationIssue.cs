namespace Pvm.Domain.Validation;

public sealed record ValidationIssue(
    string Code,
    string Message,
    ValidationSeverity Severity,
    string FixLocation);
