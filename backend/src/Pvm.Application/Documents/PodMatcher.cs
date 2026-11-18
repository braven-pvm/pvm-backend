namespace Pvm.Application.Documents;

/// <summary>
/// The match decision for a proof of delivery (design 6.5 steps 3 to 7). The lookups of steps 1
/// and 2 happen elsewhere; this class gets their hits and decides. A number alone never files a
/// document: one invoice must remain and a second, independent signal must agree with it.
/// Every review outcome carries all in-scope invoices the lookups found, so a reviewer sees them all.
/// </summary>
public static class PodMatcher
{
    /// <summary>The oldest an invoice may be, in days before the delivery date, to be filed without review.</summary>
    public const int MaxDaysFromInvoiceToDelivery = 60;

    private const string InvoiceKind = "invoice";

    public static MatchOutcome Decide(PodExtraction extraction, IReadOnlyList<CandidateHit> hits)
    {
        // Step 3: the distinct invoices, voided and cancelled ones left out.
        var inScope = hits
            .Where(hit => !InvoiceStatuses.IsOutOfScope(hit.Invoice.Status))
            .ToList();
        var invoices = Distinct(inScope.Select(hit => hit.Invoice));

        if (invoices.Count == 0)
        {
            return new Review(ReviewReason.NoCandidate, []);
        }

        // Step 4a: an invoice number printed on the paper outranks order numbers. A split shipment
        // shares its sales order and PO across invoices, so only the invoices hit by an invoice
        // number are candidates when there is one.
        var byInvoiceNumber = Distinct(inScope
            .Where(hit => string.Equals(hit.MatchedOn, InvoiceKind, StringComparison.OrdinalIgnoreCase))
            .Select(hit => hit.Invoice));

        InvoiceCandidate invoice;
        if (byInvoiceNumber.Count > 0)
        {
            if (byInvoiceNumber.Count > 1)
            {
                return new Review(ReviewReason.MultipleCandidates, invoices);
            }

            invoice = byInvoiceNumber[0];
        }
        else if (invoices.Count == 1)
        {
            invoice = invoices[0];
        }
        else
        {
            // Step 4b: order-derived candidates only. The delivery date narrows them only when
            // exactly one is dated on or before it; there is no "nearest" choice.
            if (extraction.DeliveryDate is not { } delivered)
            {
                return new Review(ReviewReason.MultipleCandidates, invoices);
            }

            var onOrBefore = invoices.Where(candidate => candidate.Date <= delivered).ToList();
            if (onOrBefore.Count != 1)
            {
                return new Review(ReviewReason.MultipleCandidates, invoices);
            }

            invoice = onOrBefore[0];
        }

        // Step 5: one invoice. When the delivery date was read, the invoice must be dated on or
        // before it and not long before it; otherwise nothing ties the paper to this invoice.
        if (extraction.DeliveryDate is { } deliveredOn
            && (invoice.Date > deliveredOn || invoice.Date < deliveredOn.AddDays(-MaxDaysFromInvoiceToDelivery)))
        {
            return ReviewWith(ReviewReason.NoSecondSignal, invoice, invoices);
        }

        // Step 6: it needs a second signal. A name that agrees with the customer or ship-to name is
        // one; a name that agrees only with the parent customer (a whole chain) is not, and falls
        // through to the number kinds; a name that agrees with none of them is a conflict.
        if (!string.IsNullOrWhiteSpace(extraction.ClientName))
        {
            if (CompanyNameComparer.AgreesDirectly(extraction.ClientName, invoice))
            {
                return new Matched(invoice);
            }

            if (!CompanyNameComparer.Agrees(extraction.ClientName, invoice))
            {
                return ReviewWith(ReviewReason.ClientNameConflict, invoice, invoices);
            }
        }

        var kindsThatFoundIt = inScope
            .Where(hit => SameInvoice(hit.Invoice, invoice))
            .Select(hit => hit.MatchedOn)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return kindsThatFoundIt >= 2
            ? new Matched(invoice)
            : ReviewWith(ReviewReason.NoSecondSignal, invoice, invoices);
    }

    /// <summary>A review with the invoice the matcher settled on first, then every other in-scope invoice.</summary>
    private static Review ReviewWith(ReviewReason reason, InvoiceCandidate chosen, IReadOnlyList<InvoiceCandidate> all)
        => new(reason, [chosen, .. all.Where(other => !SameInvoice(other, chosen))]);

    private static bool SameInvoice(InvoiceCandidate left, InvoiceCandidate right)
        => string.Equals(left.ReferenceNumber, right.ReferenceNumber, StringComparison.OrdinalIgnoreCase);

    private static List<InvoiceCandidate> Distinct(IEnumerable<InvoiceCandidate> candidates)
        => candidates
            .DistinctBy(candidate => candidate.ReferenceNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
