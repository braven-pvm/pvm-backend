using Pvm.Api.Features.Reports;
using Pvm.Infrastructure.Reporting.SalesReport;

namespace Pvm.Api.Tests.Reports;

public sealed class SalesReportEndpointsTests
{
    private static readonly DateOnly Today = new(2026, 11, 18); // Financial year 2027.

    [Fact]
    public void With_no_years_the_report_shows_this_financial_year_and_the_year_before()
    {
        var (filter, problem) = SalesReportEndpoints.ReadFilter(new SalesReportQuery(), Today);

        Assert.Null(problem);
        Assert.Equal([2026, 2027], filter!.FinancialYears);
        Assert.Equal(SalesReportMeasure.Amount, filter.Measure);
    }

    [Fact]
    public void The_filters_are_read_and_trimmed()
    {
        var query = new SalesReportQuery
        {
            Years = "2025, 2026", Channel = " Pharmacy ", Rep = "R1", Province = "GP", ItemGroup = "BARS", Product = "BAR01", Warehouse = "JHB", Measure = "quantity",
        };

        var (filter, problem) = SalesReportEndpoints.ReadFilter(query, Today);

        Assert.Null(problem);
        int[] years = [2025, 2026];
        Assert.Equal(new SalesReportFilter(years, "Pharmacy", "R1", "GP", "BARS", "BAR01", "JHB", SalesReportMeasure.Quantity), filter! with { FinancialYears = years });
        Assert.Equal(years, filter.FinancialYears);
    }

    [Theory]
    [InlineData("2026,x", null, "Give the financial years as 2026,2027.")]
    [InlineData("1999", null, "Give the financial years as 2026,2027.")]
    [InlineData("2024,2025,2026,2027", null, "A report shows at most 3 financial years.")]
    [InlineData("2027", "value", "The measure is amount or quantity.")]
    public void A_wrong_filter_gives_a_message(string years, string? measure, string message)
    {
        var (filter, problem) = SalesReportEndpoints.ReadFilter(new SalesReportQuery { Years = years, Measure = measure }, Today);

        Assert.Null(filter);
        Assert.Equal(message, problem);
    }

    [Theory]
    [InlineData("by-rep", null, SalesReportView.ByRep)]
    [InlineData("by-product", null, SalesReportView.ByProduct)]
    [InlineData("by-product", "rep", SalesReportView.ByProductThenRep)]
    [InlineData("customers", null, SalesReportView.Customers)]
    public void The_view_comes_from_the_route(string view, string? then, SalesReportView expected)
    {
        Assert.Equal(expected, SalesReportEndpoints.ReadView(view, then));
    }

    [Fact]
    public void An_unknown_view_is_not_found()
    {
        Assert.Null(SalesReportEndpoints.ReadView("by-warehouse", null));
    }

    [Fact]
    public void Open_rows_are_a_comma_list_of_keys()
    {
        Assert.Equal(["R1", "R1|RETAIL"], SalesReportEndpoints.ReadOpen("R1, R1|RETAIL,,"));
        Assert.Empty(SalesReportEndpoints.ReadOpen(null));
    }

    [Fact]
    public void The_csv_file_name_names_the_view_and_the_years()
    {
        Assert.Equal("sales-by-rep-FY2026-FY2027.csv", SalesReportEndpoints.CsvFileName("by-rep", [2026, 2027]));
    }
}
