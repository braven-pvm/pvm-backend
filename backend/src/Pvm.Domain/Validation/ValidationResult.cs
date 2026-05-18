namespace Pvm.Domain.Validation;

public sealed class ValidationResult(IReadOnlyList<ValidationIssue> issues)
{
    public IReadOnlyList<ValidationIssue> Issues { get; } = issues;

    public bool CanSubmit => Issues.All(issue => issue.Severity != ValidationSeverity.Blocking);
}
