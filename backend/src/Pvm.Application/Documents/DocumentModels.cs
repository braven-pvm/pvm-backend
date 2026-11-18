namespace Pvm.Application.Documents;

/// <summary>Where a captured document is in its life (design 6.7).</summary>
public enum DocumentState
{
    Received,
    Processing,
    Filed,
    NeedsReview,
    Failed,
}

/// <summary>Why a document is on the review list instead of filed (design 6.5 step 7).</summary>
public enum ReviewReason
{
    NoCandidate,
    MultipleCandidates,
    NoSecondSignal,
    ClientNameConflict,
    ReadFailure,
}

/// <summary>The fields Claude reads from a proof-of-delivery paper. Each number field is a list, because a paper can show more than one candidate.</summary>
/// <param name="CustomerOrderNumbers">Client purchase-order numbers; Acumatica holds them on the sales order as the customer order number.</param>
/// <param name="Note">Free text for anything the reader found unclear.</param>
public sealed record PodExtraction(
    IReadOnlyList<string> InvoiceNumbers,
    IReadOnlyList<string> SalesOrderNumbers,
    IReadOnlyList<string> CustomerOrderNumbers,
    string? ClientName,
    DateOnly? DeliveryDate,
    bool SignaturePresent,
    string? Note);

/// <summary>An Acumatica document of the type "Invoice" that an extracted number points to.</summary>
public sealed record InvoiceCandidate(
    string ReferenceNumber,
    DateOnly Date,
    string CustomerId,
    string CustomerName,
    string? ParentCustomerName,
    string? ShipToName,
    string Status);

/// <summary>One lookup result: the invoice and the kind of number that found it.</summary>
/// <param name="MatchedOn"><c>"invoice"</c>, <c>"salesOrder"</c>, or <c>"customerOrder"</c>.</param>
public sealed record CandidateHit(InvoiceCandidate Invoice, string MatchedOn);

/// <summary>The match decision for a document.</summary>
public abstract record MatchOutcome;

/// <summary>Exactly one invoice with a second, independent signal: the document can be filed.</summary>
public sealed record Matched(InvoiceCandidate Invoice) : MatchOutcome;

/// <summary>The document goes to the review list with a reason and the candidates the backend found.</summary>
public sealed record Review(ReviewReason Reason, IReadOnlyList<InvoiceCandidate> Candidates) : MatchOutcome;
