namespace Pvm.Application.Documents;

/// <summary>
/// The match decision for a proof of delivery (design 6.5 steps 3 to 7). The lookups of steps 1
/// and 2 happen elsewhere; this class gets their hits and decides. A number alone never files a
/// document: one invoice must remain and a second, independent signal must agree with it.
/// </summary>
public static class PodMatcher
{
    private static readonly HashSet<string> OutOfScopeStatuses =
        new(["Voided", "Canceled", "Cancelled"], StringComparer.OrdinalIgnoreCase);

    public static MatchOutcome Decide(PodExtraction extraction, IReadOnlyList<CandidateHit> hits)
    {
        // Step 3: the distinct invoices, voided and cancelled ones left out.
        var inScope = hits
            .Where(hit => !OutOfScopeStatuses.Contains(hit.Invoice.Status.Trim()))
            .ToList();
        var invoices = inScope
            .Select(hit => hit.Invoice)
            .DistinctBy(invoice => invoice.ReferenceNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (invoices.Count == 0)
        {
            return new Review(ReviewReason.NoCandidate, []);
        }

        // Step 4: more than one invoice; keep the nearest on or before the delivery date.
        var remaining = invoices;
        if (remaining.Count > 1 && extraction.DeliveryDate is { } delivered)
        {
            var onOrBefore = remaining.Where(invoice => invoice.Date <= delivered).ToList();
            if (onOrBefore.Count > 0)
            {
                var nearest = onOrBefore.Max(invoice => invoice.Date);
                remaining = onOrBefore.Where(invoice => invoice.Date == nearest).ToList();
            }
        }

        if (remaining.Count > 1)
        {
            return new Review(ReviewReason.MultipleCandidates, invoices);
        }

        // Step 5: one invoice; it needs a second signal.
        var invoice = remaining[0];
        if (!string.IsNullOrWhiteSpace(extraction.ClientName))
        {
            return CompanyNameComparer.Agrees(extraction.ClientName, invoice)
                ? new Matched(invoice)
                : new Review(ReviewReason.ClientNameConflict, [invoice]);
        }

        var kindsThatFoundIt = inScope
            .Where(hit => string.Equals(hit.Invoice.ReferenceNumber, invoice.ReferenceNumber, StringComparison.OrdinalIgnoreCase))
            .Select(hit => hit.MatchedOn)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return kindsThatFoundIt >= 2
            ? new Matched(invoice)
            : new Review(ReviewReason.NoSecondSignal, [invoice]);
    }
}
