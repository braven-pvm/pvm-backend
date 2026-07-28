using Pvm.Api.Features.Invoices;
using Pvm.Domain.Validation;

namespace Pvm.Api.Tests.Invoices;

public sealed class InvoiceCandidateReadinessTests
{
    [Fact]
    public void CanSubmit_ReturnsFalseWhenCandidateHasNoMatchedPurchaseOrder()
    {
        var result = InvoiceCandidateReadiness.CanSubmit(
            new ValidationResult([]),
            matchedPurchaseOrderId: null,
            candidateStatus: "Ready",
            attemptStatuses: []);

        Assert.False(result);
    }

    [Fact]
    public void CanSubmit_ReturnsTrueWhenCandidateIsValidMatchedAndNotPreviouslySubmitted()
    {
        var result = InvoiceCandidateReadiness.CanSubmit(
            new ValidationResult([]),
            matchedPurchaseOrderId: Guid.NewGuid(),
            candidateStatus: "Ready",
            attemptStatuses: []);

        Assert.True(result);
    }

    [Fact]
    public void CanSubmit_ReturnsFalseWhenCandidateWasRejectedAndNeedsAdminReview()
    {
        var result = InvoiceCandidateReadiness.CanSubmit(
            new ValidationResult([]),
            matchedPurchaseOrderId: Guid.NewGuid(),
            candidateStatus: "Rejected",
            attemptStatuses: ["Rejected"]);

        Assert.False(result);
    }
}
