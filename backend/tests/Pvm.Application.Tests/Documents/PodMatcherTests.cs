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
}
