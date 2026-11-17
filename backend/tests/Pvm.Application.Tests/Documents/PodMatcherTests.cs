using Pvm.Application.Documents;
using Xunit;

namespace Pvm.Application.Tests.Documents;

public class PodMatcherTests
{
    private static InvoiceCandidate Invoice(
        string reference,
        string date = "2026-11-10",
        string customerName = "Shoprite Checkers Pty Ltd",
        string status = "Open")
        => new(reference, DateOnly.Parse(date), "C000123", customerName, null, null, status);

    private static PodExtraction Extraction(string? clientName = null, string? deliveryDate = null)
        => new(
            ["INV001"],
            [],
            [],
            clientName,
            deliveryDate is null ? null : DateOnly.Parse(deliveryDate),
            true,
            null);

    [Fact]
    public void NoHits_ReviewNoCandidate()
    {
        var outcome = PodMatcher.Decide(Extraction("Shoprite Checkers"), []);

        var review = Assert.IsType<Review>(outcome);
        Assert.Equal(ReviewReason.NoCandidate, review.Reason);
        Assert.Empty(review.Candidates);
    }

    [Fact]
    public void OneInvoiceByNumberOnly_NoClientName_ReviewNoSecondSignal()
    {
        var invoice = Invoice("INV001");

        var outcome = PodMatcher.Decide(Extraction(), [new CandidateHit(invoice, "invoice")]);

        var review = Assert.IsType<Review>(outcome);
        Assert.Equal(ReviewReason.NoSecondSignal, review.Reason);
        Assert.Equal(invoice, Assert.Single(review.Candidates));
    }

    [Fact]
    public void OneInvoiceByNumber_ClientNameAgrees_Matched()
    {
        var invoice = Invoice("INV001");

        var outcome = PodMatcher.Decide(
            Extraction("SHOPRITE CHECKERS (PTY) LTD"),
            [new CandidateHit(invoice, "invoice")]);

        Assert.Equal(invoice, Assert.IsType<Matched>(outcome).Invoice);
    }

    [Fact]
    public void OneInvoice_HitByInvoiceAndCustomerOrder_Matched()
    {
        var invoice = Invoice("INV001");

        var outcome = PodMatcher.Decide(
            Extraction(),
            [new CandidateHit(invoice, "invoice"), new CandidateHit(invoice, "customerOrder")]);

        Assert.Equal(invoice, Assert.IsType<Matched>(outcome).Invoice);
    }

    [Fact]
    public void OneInvoice_ClientNameDisagrees_ReviewClientNameConflict()
    {
        var invoice = Invoice("INV001");

        var outcome = PodMatcher.Decide(
            Extraction("Pick n Pay"),
            [new CandidateHit(invoice, "invoice"), new CandidateHit(invoice, "customerOrder")]);

        var review = Assert.IsType<Review>(outcome);
        Assert.Equal(ReviewReason.ClientNameConflict, review.Reason);
        Assert.Equal(invoice, Assert.Single(review.Candidates));
    }

    [Fact]
    public void TwoInvoices_DeliveryDateSelectsNearestOnOrBefore_Matched()
    {
        var earlier = Invoice("INV001", "2026-11-10");
        var later = Invoice("INV002", "2026-11-14");

        var outcome = PodMatcher.Decide(
            Extraction("Shoprite Checkers", "2026-11-12"),
            [new CandidateHit(earlier, "salesOrder"), new CandidateHit(later, "salesOrder")]);

        Assert.Equal(earlier, Assert.IsType<Matched>(outcome).Invoice);
    }

    [Fact]
    public void TwoInvoices_NoDeliveryDate_ReviewMultipleCandidates()
    {
        var first = Invoice("INV001", "2026-11-10");
        var second = Invoice("INV002", "2026-11-14");

        var outcome = PodMatcher.Decide(
            Extraction("Shoprite Checkers"),
            [new CandidateHit(first, "salesOrder"), new CandidateHit(second, "salesOrder")]);

        var review = Assert.IsType<Review>(outcome);
        Assert.Equal(ReviewReason.MultipleCandidates, review.Reason);
        Assert.Equal([first, second], review.Candidates);
    }

    [Fact]
    public void TwoInvoices_SameDateOnOrBeforeDelivery_ReviewMultipleCandidates()
    {
        var first = Invoice("INV001", "2026-11-10");
        var second = Invoice("INV002", "2026-11-10");

        var outcome = PodMatcher.Decide(
            Extraction("Shoprite Checkers", "2026-11-12"),
            [new CandidateHit(first, "salesOrder"), new CandidateHit(second, "salesOrder")]);

        var review = Assert.IsType<Review>(outcome);
        Assert.Equal(ReviewReason.MultipleCandidates, review.Reason);
    }

    [Theory]
    [InlineData("Voided")]
    [InlineData("Canceled")]
    [InlineData("cancelled")]
    public void VoidedOrCancelledInvoice_IsIgnored(string status)
    {
        var voided = Invoice("INV001", status: status);

        var outcome = PodMatcher.Decide(Extraction("Shoprite Checkers"), [new CandidateHit(voided, "invoice")]);

        var review = Assert.IsType<Review>(outcome);
        Assert.Equal(ReviewReason.NoCandidate, review.Reason);
        Assert.Empty(review.Candidates);
    }

    private static PodExtraction Paper(
        string[] invoiceNumbers,
        string[] salesOrders,
        string[] customerOrders,
        string? clientName = null,
        string? deliveryDate = null)
        => new(
            invoiceNumbers,
            salesOrders,
            customerOrders,
            clientName,
            deliveryDate is null ? null : DateOnly.Parse(deliveryDate),
            true,
            null);

    // Split shipment: invoices A (Nov 10) and B (Nov 12) both from sales order S / PO P, delivered Nov 12.
    private static readonly InvoiceCandidate SplitA = Invoice("INV-A", "2026-11-10");
    private static readonly InvoiceCandidate SplitB = Invoice("INV-B", "2026-11-12");

    [Fact]
    public void SplitShipment_PaperShowsInvoiceA_MatchedA()
    {
        var outcome = PodMatcher.Decide(
            Paper(["INV-A"], ["S"], ["P"], deliveryDate: "2026-11-12"),
            [
                new CandidateHit(SplitA, "invoice"),
                new CandidateHit(SplitA, "salesOrder"),
                new CandidateHit(SplitA, "customerOrder"),
                new CandidateHit(SplitB, "salesOrder"),
                new CandidateHit(SplitB, "customerOrder"),
            ]);

        Assert.Equal(SplitA, Assert.IsType<Matched>(outcome).Invoice);
    }

    [Fact]
    public void SplitShipment_PaperShowsInvoiceB_MatchedB()
    {
        var outcome = PodMatcher.Decide(
            Paper(["INV-B"], ["S"], ["P"], deliveryDate: "2026-11-12"),
            [
                new CandidateHit(SplitB, "invoice"),
                new CandidateHit(SplitB, "salesOrder"),
                new CandidateHit(SplitB, "customerOrder"),
                new CandidateHit(SplitA, "salesOrder"),
                new CandidateHit(SplitA, "customerOrder"),
            ]);

        Assert.Equal(SplitB, Assert.IsType<Matched>(outcome).Invoice);
    }

    [Fact]
    public void GrnWithOnlyCustomerOrder_TwoInvoicesOnOrBeforeDelivery_ReviewMultipleCandidatesWithBoth()
    {
        var outcome = PodMatcher.Decide(
            Paper([], [], ["P"], "Shoprite Checkers", "2026-11-12"),
            [new CandidateHit(SplitA, "customerOrder"), new CandidateHit(SplitB, "customerOrder")]);

        var review = Assert.IsType<Review>(outcome);
        Assert.Equal(ReviewReason.MultipleCandidates, review.Reason);
        Assert.Equal([SplitA, SplitB], review.Candidates);
    }

    [Fact]
    public void OrderHits_OnlyOneOnOrBeforeDelivery_ProceedsToSignalCheck()
    {
        var before = Invoice("INV001", "2026-11-10");
        var after = Invoice("INV002", "2026-11-14");
        var hits = new[] { new CandidateHit(before, "customerOrder"), new CandidateHit(after, "customerOrder") };

        // No name and one kind of number: no second signal; the review carries both invoices.
        var noSignal = PodMatcher.Decide(Paper([], [], ["P"], deliveryDate: "2026-11-12"), hits);
        var review = Assert.IsType<Review>(noSignal);
        Assert.Equal(ReviewReason.NoSecondSignal, review.Reason);
        Assert.Equal(before, review.Candidates[0]);
        Assert.Contains(after, review.Candidates);
        Assert.Equal(2, review.Candidates.Count);

        // A name that agrees is the second signal.
        var named = PodMatcher.Decide(Paper([], [], ["P"], "Shoprite Checkers", "2026-11-12"), hits);
        Assert.Equal(before, Assert.IsType<Matched>(named).Invoice);
    }

    [Fact]
    public void InvoiceNumberHitsTwoInvoices_ReviewMultipleCandidates()
    {
        var first = Invoice("INV001", "2026-11-10");
        var second = Invoice("INV002", "2026-11-11");

        var outcome = PodMatcher.Decide(
            Paper(["INV001", "INV002"], ["S"], [], "Shoprite Checkers", "2026-11-12"),
            [
                new CandidateHit(first, "invoice"),
                new CandidateHit(second, "invoice"),
                new CandidateHit(first, "salesOrder"),
            ]);

        var review = Assert.IsType<Review>(outcome);
        Assert.Equal(ReviewReason.MultipleCandidates, review.Reason);
        Assert.Equal(2, review.Candidates.Count);
    }

    [Fact]
    public void ReviewOutcomes_CarryAllInScopeInvoices()
    {
        var byNumber = Invoice("INV001", "2026-11-10");
        var byOrder = Invoice("INV002", "2026-11-11");
        var voided = Invoice("INV003", "2026-11-11", status: "Voided");

        // The invoice-number hit is the only candidate, but its name disagrees: the review still lists every in-scope invoice.
        var outcome = PodMatcher.Decide(
            Paper(["INV001"], ["S"], [], "Pick n Pay", "2026-11-12"),
            [
                new CandidateHit(byNumber, "invoice"),
                new CandidateHit(byOrder, "salesOrder"),
                new CandidateHit(voided, "salesOrder"),
            ]);

        var review = Assert.IsType<Review>(outcome);
        Assert.Equal(ReviewReason.ClientNameConflict, review.Reason);
        Assert.Equal(byNumber, review.Candidates[0]);
        Assert.Equal(2, review.Candidates.Count);
        Assert.DoesNotContain(voided, review.Candidates);
    }

    private static InvoiceCandidate ChainInvoice(string reference, string date = "2026-11-10")
        => new(reference, DateOnly.Parse(date), "C000456", "Checkers Hyper Brackenfell", "Shoprite Holdings Ltd", null, "Open");

    [Fact]
    public void NameAgreesOnlyWithParent_IsNotASecondSignal()
    {
        var invoice = ChainInvoice("INV001");

        var outcome = PodMatcher.Decide(
            Paper(["INV001"], [], [], "Shoprite Holdings"),
            [new CandidateHit(invoice, "invoice")]);

        var review = Assert.IsType<Review>(outcome);
        Assert.Equal(ReviewReason.NoSecondSignal, review.Reason);
        Assert.Equal(invoice, Assert.Single(review.Candidates));
    }

    [Fact]
    public void NameAgreesOnlyWithParent_FallsThroughToNumberKinds()
    {
        var invoice = ChainInvoice("INV001");

        var outcome = PodMatcher.Decide(
            Paper(["INV001"], [], ["P"], "Shoprite Holdings"),
            [new CandidateHit(invoice, "invoice"), new CandidateHit(invoice, "customerOrder")]);

        Assert.Equal(invoice, Assert.IsType<Matched>(outcome).Invoice);
    }

    [Fact]
    public void NameDisagreesWithCustomerShipToAndParent_ReviewClientNameConflict()
    {
        var invoice = ChainInvoice("INV001");

        var outcome = PodMatcher.Decide(
            Paper(["INV001"], [], ["P"], "Pick n Pay"),
            [new CandidateHit(invoice, "invoice"), new CandidateHit(invoice, "customerOrder")]);

        Assert.Equal(ReviewReason.ClientNameConflict, Assert.IsType<Review>(outcome).Reason);
    }

    [Fact]
    public void SingleCandidate_DatedAfterDelivery_ReviewNoSecondSignal()
    {
        var invoice = Invoice("INV001", "2026-11-14");

        var outcome = PodMatcher.Decide(
            Paper(["INV001"], [], ["P"], "Shoprite Checkers", "2026-11-12"),
            [new CandidateHit(invoice, "invoice"), new CandidateHit(invoice, "customerOrder")]);

        var review = Assert.IsType<Review>(outcome);
        Assert.Equal(ReviewReason.NoSecondSignal, review.Reason);
        Assert.Equal(invoice, Assert.Single(review.Candidates));
    }

    [Fact]
    public void SingleCandidate_MoreThan60DaysBeforeDelivery_ReviewNoSecondSignal()
    {
        var invoice = Invoice("INV001", "2026-09-12");

        var outcome = PodMatcher.Decide(
            Paper(["INV001"], [], ["P"], "Shoprite Checkers", "2026-11-12"),
            [new CandidateHit(invoice, "invoice"), new CandidateHit(invoice, "customerOrder")]);

        var review = Assert.IsType<Review>(outcome);
        Assert.Equal(ReviewReason.NoSecondSignal, review.Reason);
        Assert.Equal(invoice, Assert.Single(review.Candidates));
    }

    [Fact]
    public void SingleCandidate_Exactly60DaysBeforeDelivery_Matched()
    {
        Assert.Equal(60, PodMatcher.MaxDaysFromInvoiceToDelivery);
        var invoice = Invoice("INV001", "2026-09-13");

        var outcome = PodMatcher.Decide(
            Paper(["INV001"], [], [], "Shoprite Checkers", "2026-11-12"),
            [new CandidateHit(invoice, "invoice")]);

        Assert.Equal(invoice, Assert.IsType<Matched>(outcome).Invoice);
    }

    [Fact]
    public void SingleCandidate_NoDeliveryDate_NoDateCheck()
    {
        var invoice = Invoice("INV001", "2025-01-01");

        var outcome = PodMatcher.Decide(
            Paper(["INV001"], [], [], "Shoprite Checkers"),
            [new CandidateHit(invoice, "invoice")]);

        Assert.Equal(invoice, Assert.IsType<Matched>(outcome).Invoice);
    }
}
