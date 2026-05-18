using Pvm.Domain.Validation;

namespace Pvm.Domain.Tests;

public sealed class ValidationResultTests
{
    [Fact]
    public void CanSubmit_is_false_when_any_blocking_issue_exists()
    {
        var result = new ValidationResult([
            new ValidationIssue("missing-gln", "Store/DC GLN is missing.", ValidationSeverity.Blocking, "integration-config")
        ]);

        Assert.False(result.CanSubmit);
    }

    [Fact]
    public void CanSubmit_is_true_when_result_has_only_warnings()
    {
        var result = new ValidationResult([
            new ValidationIssue("unverified-uom", "UOM mapping is unverified in QA.", ValidationSeverity.Warning, "integration-config")
        ]);

        Assert.True(result.CanSubmit);
    }
}
