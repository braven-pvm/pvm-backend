using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;

namespace Pvm.Application.Submissions;

public sealed record InvoiceSubmissionSnapshot(
    Guid InvoiceCandidateId,
    CanonicalInvoice Invoice,
    ValidationResult Validation,
    bool HasMatchedPurchaseOrder,
    string IdempotencyKey,
    string? SourceJson,
    string CanonicalJson,
    string SourceVersion,
    string CandidateStatus = "Unknown");
