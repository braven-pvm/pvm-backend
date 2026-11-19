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

    private static InvoiceCandidate DisChemMondeor()
        => new(
            "INV190931",
            new DateOnly(2026, 10, 6),
            "C000456",
            "DIS-CHEM PHARMACIES LTD MONDEOR S007",
            "DIS-CHEM PHARMACIES LTD CCO3",
            null,
            "Open");

    [Fact]
    public void A_store_name_with_a_word_in_between_agrees_directly()
    {
        // Production case: the paper says "Dis-Chem Mondeor S007"; Acumatica has
        // "DIS-CHEM PHARMACIES LTD MONDEOR S007".
        Assert.True(CompanyNameComparer.AgreesDirectly("Dis-Chem Mondeor S007", DisChemMondeor()));
    }

    [Fact]
    public void Words_in_between_allowed_when_not_a_generic_word()
    {
        Assert.True(CompanyNameComparer.AgreesDirectly(
            "Checkers Brackenfell",
            Invoice("Shoprite Checkers Hyper Brackenfell")));
    }

    [Fact]
    public void A_different_branch_of_the_same_chain_does_not_agree()
    {
        var invoice = Invoice("DIS-CHEM PHARMACIES LTD SANDTON S012", parentCustomerName: "DIS-CHEM PHARMACIES LTD CCO3");

        Assert.False(CompanyNameComparer.AgreesDirectly("Dis-Chem Mondeor", invoice));
        Assert.False(CompanyNameComparer.AgreesDirectly("Dis-Chem Mondeor S007", invoice));
    }

    [Fact]
    public void The_words_in_the_wrong_order_do_not_agree()
    {
        Assert.False(CompanyNameComparer.Agrees("Mondeor Dis-Chem", DisChemMondeor()));
    }

    [Fact]
    public void A_single_word_name_must_still_be_a_whole_word()
    {
        Assert.True(CompanyNameComparer.AgreesDirectly("Shoprite", Invoice("Shoprite Checkers Pty Ltd")));
        Assert.False(CompanyNameComparer.AgreesDirectly("Shoprite", Invoice("Checkers Hyper Brackenfell")));
        Assert.False(CompanyNameComparer.AgreesDirectly("Shop", Invoice("Shoprite Checkers Pty Ltd")));
    }

    [Fact]
    public void A_generic_word_is_kept_when_dropping_it_would_leave_one_word()
    {
        // "Spar Group" must not shrink to "Spar" and agree with every Spar store.
        Assert.False(CompanyNameComparer.Agrees("Spar Pinetown", Invoice("Spar Group")));
        Assert.False(CompanyNameComparer.AgreesDirectly("Spar Pinetown", Invoice("Spar Group")));
        Assert.False(CompanyNameComparer.Agrees("Pharmacy Mondeor", DisChemMondeor()));
        Assert.True(CompanyNameComparer.AgreesDirectly("Dis-Chem Mondeor S007", DisChemMondeor()));
    }

    [Fact]
    public void A_name_of_only_generic_words_keeps_its_words()
    {
        Assert.True(CompanyNameComparer.AgreesDirectly("The Pharmacy", Invoice("THE PHARMACY")));
        Assert.False(CompanyNameComparer.AgreesDirectly("The Pharmacy", Invoice("Dis-Chem Pharmacies Ltd")));
    }
}
