using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Pvm.Api.Features.Reports;
using Pvm.Infrastructure.Reporting.SalesReport;

namespace Pvm.Api.Tests.Reports;

public sealed class SalesPivotEndpointsTests
{
    private static readonly DateOnly Today = new(2026, 11, 19); // Month 9 of financial year 2027.

    private static QueryCollection Query(string query) => new(QueryHelpers.ParseQuery(query));

    [Fact]
    public void With_no_months_the_filter_is_the_financial_year_to_date()
    {
        var (request, problem) = SalesPivotEndpoints.ReadRequest(Query(""), Today);

        Assert.Null(problem);
        Assert.Equal((202701, 202709), (request!.Filter.From, request.Filter.To));
        Assert.Equal([SalesDimension.Rep, SalesDimension.Customer, SalesDimension.Product], request.Rows);
        Assert.Equal(SalesPivotColumns.Months, request.Columns);
        Assert.Equal(SalesPivotMeasure.Amount, request.Filter.Measure);
        Assert.False(request.Compare);
        Assert.Empty(request.Open!);
        Assert.Empty(request.Filter.Include);
    }

    [Fact]
    public void The_request_reads_months_rows_selections_and_options()
    {
        var (request, problem) = SalesPivotEndpoints.ReadRequest(
            Query("from=2025-03&to=2026-02&rows=product,rep&rep=R1,R2&rep=R3&customer=C1&channel=Other&measure=customers&columns=years&compare=1&sort=label&open=BAR01,BAR01|R1"),
            Today);

        Assert.Null(problem);
        Assert.Equal((202601, 202612), (request!.Filter.From, request.Filter.To));
        Assert.Equal([SalesDimension.Product, SalesDimension.Rep], request.Rows);
        Assert.Equal(["R1", "R2", "R3"], request.Filter.Include[SalesDimension.Rep]);
        Assert.Equal(["C1"], request.Filter.Include[SalesDimension.Customer]);
        Assert.Equal(["Other"], request.Filter.Include[SalesDimension.Channel]);
        Assert.Equal(SalesPivotMeasure.Customers, request.Filter.Measure);
        Assert.Equal(SalesPivotColumns.Years, request.Columns);
        Assert.True(request.Compare);
        Assert.True(request.SortByLabel);
        Assert.Equal(["BAR01", "BAR01|R1"], request.Open!);
    }

    [Fact]
    public void Open_star_opens_every_level()
    {
        var (request, _) = SalesPivotEndpoints.ReadRequest(Query("rows=rep,customer&open=*"), Today);

        Assert.Null(request!.Open);
    }

    [Theory]
    [InlineData("from=2026-13", "Give the months as 2026-03.")]
    [InlineData("from=2027-01&to=2026-03", "The first month must be before the last month.")]
    [InlineData("from=2022-03&to=2026-02", "A report shows at most 36 months.")]
    [InlineData("measure=value", "The measure is amount, quantity or customers.")]
    [InlineData("rows=rep,town", "There is no dimension town. Use rep, repAtSale, channel, customerGroup, customer, province, itemGroup, product, warehouse.")]
    [InlineData("rows=rep,rep", "The dimension rep is in the rows two times.")]
    [InlineData("rows=rep,customer,product,itemGroup,province,channel", "The rows have at most 5 dimensions.")]
    [InlineData("columns=weeks", "The columns are months, years or none.")]
    public void A_wrong_request_gives_a_message(string query, string message)
    {
        var (request, problem) = SalesPivotEndpoints.ReadRequest(Query(query), Today);

        Assert.Null(request);
        Assert.Equal(message, problem);
    }

    [Fact]
    public void The_csv_file_name_names_the_rows_and_the_months()
    {
        var (request, _) = SalesPivotEndpoints.ReadRequest(Query("rows=rep,customer&from=2026-03&to=2026-11"), Today);

        Assert.Equal("sales-rep-customer-2026-03-to-2026-11.csv", SalesPivotEndpoints.CsvFileName(request!));
    }
}
