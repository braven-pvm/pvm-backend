using Pvm.Infrastructure.Reporting;

namespace Pvm.Infrastructure.Tests.Reporting;

public sealed class CustomerSalesRepParserTests
{
    // Made-up values in the shape of the contract API entity Customer with $expand=Salespersons.
    internal static string Customer(string id, params (string Rep, string Name, bool IsDefault)[] reps) => $$"""
        {
          "id": "{{Guid.NewGuid()}}", "rowNumber": 1,
          "CustomerID": { "value": "{{id}}" },
          "Salespersons": [ {{string.Join(",", reps.Select(rep =>
              $$"""{ "SalespersonID": { "value": "{{rep.Rep}}" }, "Name": { "value": "{{rep.Name}}" }, "Default": { "value": {{(rep.IsDefault ? "true" : "false")}} } }"""))}} ]
        }
        """;

    internal static string Page(params string[] customers) => $"[ {string.Join(",", customers)} ]";

    [Fact]
    public void The_default_salesperson_is_the_current_rep()
    {
        var json = Page(Customer("C0001", ("REP01", "First Rep", false), ("REP02", "Second Rep", true)));

        var customer = Assert.Single(CustomerSalesRepParser.ParsePage(json));

        Assert.Equal("C0001", customer.CustomerId);
        Assert.Equal("REP02", customer.SalesRepId);
        Assert.Equal("Second Rep", customer.SalesRepName);
    }

    [Fact]
    public void A_customer_without_a_default_salesperson_has_no_current_rep()
    {
        var json = Page(Customer("C0001", ("REP01", "First Rep", false)), Customer("C0002"));

        var customers = CustomerSalesRepParser.ParsePage(json);

        Assert.Equal(["C0001", "C0002"], customers.Select(customer => customer.CustomerId));
        Assert.All(customers, customer => Assert.Null(customer.SalesRepId));
    }

    [Fact]
    public void Customer_ids_are_trimmed_because_acumatica_pads_them()
    {
        var json = Page(Customer("C0001   ", ("REP01  ", "First Rep", true)));

        var customer = Assert.Single(CustomerSalesRepParser.ParsePage(json));

        Assert.Equal("C0001", customer.CustomerId);
        Assert.Equal("REP01", customer.SalesRepId);
    }

    [Fact]
    public void A_response_that_is_not_a_list_is_refused()
    {
        Assert.Throws<SellInReadException>(() => CustomerSalesRepParser.ParsePage("""{ "message": "x" }"""));
    }
}
