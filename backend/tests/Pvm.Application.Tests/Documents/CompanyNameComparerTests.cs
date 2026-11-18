using Pvm.Application.Documents;
using Xunit;

namespace Pvm.Application.Tests.Documents;

public class CompanyNameComparerTests
{
    private static InvoiceCandidate Invoice(
        string customerName,
        string? parentCustomerName = null,
        string? shipToName = null)
        => new("INV001", new DateOnly(2026, 11, 10), "C000123", customerName, parentCustomerName, shipToName, "Open");

    [Fact]
    public void Customer_name_agrees_ignoring_case_punctuation_and_suffixes()
    {
        Assert.True(CompanyNameComparer.Agrees("SHOPRITE CHECKERS (PTY) LTD", Invoice("Shoprite Checkers Pty Ltd")));
    }

    [Fact]
    public void Ship_to_name_agrees()
    {
        Assert.True(CompanyNameComparer.Agrees(
            "Checkers Hyper Brackenfell",
            Invoice("Shoprite Checkers Pty Ltd", shipToName: "CHECKERS HYPER BRACKENFELL")));
    }

    [Fact]
    public void A_different_company_does_not_agree()
    {
        Assert.False(CompanyNameComparer.Agrees("Pick n Pay", Invoice("Shoprite Checkers")));
    }

    [Fact]
    public void Parent_customer_name_agrees()
    {
        Assert.True(CompanyNameComparer.Agrees(
            "Shoprite Holdings Ltd",
            Invoice("Checkers Hyper Brackenfell", parentCustomerName: "SHOPRITE HOLDINGS LIMITED")));
    }

    [Fact]
    public void A_name_that_contains_the_other_agrees()
    {
        Assert.True(CompanyNameComparer.Agrees("Shoprite", Invoice("Shoprite Checkers Pty Ltd")));
    }

    [Fact]
    public void A_name_inside_another_word_does_not_agree()
    {
        Assert.False(CompanyNameComparer.Agrees("OK Foods", Invoice("Bokomo Foods")));
        Assert.False(CompanyNameComparer.Agrees("OK", Invoice("Bokomo Foods")));
    }

    [Fact]
    public void A_blank_name_never_agrees()
    {
        Assert.False(CompanyNameComparer.Agrees("  (Pty) Ltd ", Invoice("Shoprite Checkers Pty Ltd")));
    }

    [Fact]
    public void Parent_customer_name_is_not_a_direct_agreement()
    {
        var invoice = Invoice("Checkers Hyper Brackenfell", parentCustomerName: "SHOPRITE HOLDINGS LIMITED");

        Assert.False(CompanyNameComparer.AgreesDirectly("Shoprite Holdings Ltd", invoice));
        Assert.True(CompanyNameComparer.Agrees("Shoprite Holdings Ltd", invoice));
    }

    [Fact]
    public void Customer_or_ship_to_name_is_a_direct_agreement()
    {
        var invoice = Invoice("Shoprite Checkers Pty Ltd", "Shoprite Holdings", "Checkers Hyper Brackenfell");

        Assert.True(CompanyNameComparer.AgreesDirectly("SHOPRITE CHECKERS", invoice));
        Assert.True(CompanyNameComparer.AgreesDirectly("Checkers Hyper Brackenfell", invoice));
    }
}
