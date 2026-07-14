using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;

namespace Pvm.Api.Features.Invoices.Models;

public sealed record InvoiceCandidateSummaryResponse(
    Guid Id,
    string InvoiceNumber,
    string CustomerAccount,
    string? CustomerLocation,
    string? ShopritePurchaseOrderNumber,
    Guid? MatchedShopritePurchaseOrderId,
    string PurchaseOrderMatchStatus,
    string? StoreDcGln,
    string Status,
    bool CanSubmit,
    DateTimeOffset UpdatedAt);

public sealed record InvoiceRefreshResponse(int Received, int Created, int Updated);

public sealed record InvoiceCandidateDetailResponse(
    Guid Id,
    string Status,
    bool CanSubmit,
    object? AcumaticaInvoice,
    CanonicalInvoice? CanonicalInvoice,
    InvoiceCandidatePurchaseOrderResponse? MatchedPurchaseOrder,
    ValidationResult Validation,
    string? GeneratedXml,
    IReadOnlyList<InvoiceSubmissionAttemptResponse> Attempts);

public sealed record InvoiceCandidatePurchaseOrderResponse(
    Guid Id,
    string PurchaseOrderNumber,
    string? OrderTypeCode,
    string? OrderTypeLabel,
    string? DeliveryGln,
    string? DeliveryLocationCode,
    string? DeliveryLocationName,
    string DeliveryLocationSource,
    int LineCount);

public sealed record InvoiceSubmissionAttemptResponse(
    Guid Id,
    string InitiatedBy,
    string InitiationMode,
    string Status,
    int? ResponseStatusCode,
    string? ErrorMessage,
    string? FailureClassification,
    bool? IsRetryEligible,
    DateTimeOffset CreatedAt);
