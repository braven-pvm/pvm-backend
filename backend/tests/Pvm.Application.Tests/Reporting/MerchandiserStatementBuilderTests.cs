using Pvm.Application.Reporting.Statements;

namespace Pvm.Application.Tests.Reporting;

public sealed class MerchandiserStatementBuilderTests
{
    private static readonly DateOnly October = new(2026, 10, 1);
    private static readonly Guid MakroA = Guid.NewGuid();
    private static readonly Guid MakroB = Guid.NewGuid();

    private static StatementSellIn Sale(string customer, string cls, string? rep, decimal amount, DateOnly? month = null, string? name = null) =>
        new(customer, name ?? $"CUSTOMER {customer}", cls, rep, month ?? October, amount);

    private static StatementLineSetting Line(string label, string cls, string[] reps, string[]? customers = null, bool disChem = false) =>
        new(label, cls, reps, customers ?? [], disChem);

    private static StatementCompany Company(params StatementAreaSetting[] areas) =>
        new("Example Merchandising", "EXAMPLE MERCHANDISING", false, 5m, null, false, areas);

    [Fact]
    public void A_line_takes_the_sales_of_its_class_and_reps_only()
    {
        var company = Company(new StatementAreaSetting("Area", null, [Line("Retail", "RETAIL", ["R1", "R2"])], []));
        StatementSellIn[] sales =
        [
            Sale("C1", "RETAIL", "R1", 100m),
            Sale("C1", "RETAIL", "R1", 50m),
            Sale("C2", "RETAIL", "R2", 30m),
            Sale("C3", "RETAIL", "R3", 999m),
            Sale("C4", "PHARMACY", "R1", 999m),
            Sale("C5", "RETAIL", null, 999m),
            Sale("C1", "RETAIL", "R1", 999m, October.AddMonths(-1)),
        ];

        var line = MerchandiserStatementBuilder.Build(company, October, October, sales, []).Areas.Single().Lines.Single();

        Assert.Equal(180m, line.Amount);
        Assert.Equal([("C1", 150m), ("C2", 30m)], line.Customers.Select(customer => (customer.CustomerId, customer.Amount)));
    }

    [Fact]
    public void A_line_with_a_customer_list_takes_those_customers_only_and_no_reps_means_all_reps()
    {
        var company = Company(new StatementAreaSetting("Area", null, [Line("Checkers DC", "CHECKERS", [], ["DC1", "DC2"])], []));
        StatementSellIn[] sales = [Sale("DC1", "CHECKERS", "R1", 10m), Sale("DC2", "CHECKERS", "R9", 20m), Sale("S1", "CHECKERS", "R1", 999m)];

        var line = MerchandiserStatementBuilder.Build(company, October, October, sales, []).Areas.Single().Lines.Single();

        Assert.Equal(30m, line.Amount);
    }

    [Fact]
    public void Area_totals_add_the_makro_stores_and_the_commissions_are_rounded_to_the_cent()
    {
        var company = Company(
            new StatementAreaSetting("North", 5m, [Line("Retail", "RETAIL", ["R1"])], [MakroA]),
            new StatementAreaSetting("South", 5m, [Line("Retail", "RETAIL", ["R2"])], [MakroB]));
        StatementSellIn[] sales = [Sale("C1", "RETAIL", "R1", 100.10m), Sale("C2", "RETAIL", "R2", 200m)];
        StatementMakroValue[] makro =
        [
            new(MakroA, "Alpha Main Store", October, 50.25m, false),
            new(MakroB, "Beta Main Store", October, 10m, false),
            new(MakroB, "Beta Main Store", October.AddMonths(-1), 999m, false),
        ];

        var statement = MerchandiserStatementBuilder.Build(company, October, October, sales, makro);

        Assert.Equal([150.35m, 210m], statement.Areas.Select(area => area.Total));
        Assert.Equal([7.52m, 10.50m], statement.Areas.Select(area => area.Commission));
        Assert.Equal(360.35m, statement.GrandTotal);
        Assert.Equal(18.02m, statement.Commission);
        Assert.Equal("Alpha Main Store", statement.Areas[0].MakroStores.Single().Name);
        Assert.False(statement.MakroIsPartial);
    }

    [Fact]
    public void A_retainer_company_shows_its_fixed_amount_and_no_commission()
    {
        var company = new StatementCompany("Example", "EXAMPLE", true, null, 10000m, false,
            [new StatementAreaSetting("Area", null, [Line("Retail", "RETAIL", ["R1"])], [])]);

        var statement = MerchandiserStatementBuilder.Build(company, October, October, [Sale("C1", "RETAIL", "R1", 100m)], []);

        Assert.Null(statement.Commission);
        Assert.Equal(10000m, statement.RetainerAmount);
        Assert.Null(statement.Areas.Single().Commission);
    }

    [Fact]
    public void A_range_of_months_adds_the_months_up()
    {
        var company = Company(new StatementAreaSetting("Area", null, [Line("Distributors", "DIST", [])], []));
        StatementSellIn[] sales =
        [
            Sale("D1", "DIST", "R1", 10m, new DateOnly(2026, 5, 1)),
            Sale("D1", "DIST", "R1", 20m, new DateOnly(2026, 9, 1)),
            Sale("D1", "DIST", "R1", 999m, new DateOnly(2026, 10, 1)),
        ];

        var statement = MerchandiserStatementBuilder.Build(company, new DateOnly(2026, 5, 1), new DateOnly(2026, 9, 1), sales, []);

        Assert.Equal(30m, statement.GrandTotal);
    }

    [Fact]
    public void A_partial_makro_month_of_the_company_is_flagged()
    {
        var company = Company(new StatementAreaSetting("Area", null, [], [MakroA]));

        var statement = MerchandiserStatementBuilder.Build(company, October, October, [], [new(MakroA, "Alpha", October, 1m, true)]);

        Assert.True(statement.MakroIsPartial);
    }

    [Fact]
    public void The_dis_chem_sheet_compares_the_last_three_months_with_the_year_before()
    {
        var disChem = Line("Dis-Chem KZN", "DISCHEM", ["R1"], disChem: true);
        var company = new StatementCompany("Example", "EXAMPLE", false, 5m, null, true, [new StatementAreaSetting("Area", null, [disChem], [])]);
        StatementSellIn[] sales =
        [
            Sale("S1", "DISCHEM", "R1", 100m, new DateOnly(2026, 8, 1), "DIS-CHEM PHARMACIES LTD BALLITO S404"),
            Sale("S1", "DISCHEM", "R1", 200m, new DateOnly(2026, 10, 1), "DIS-CHEM PHARMACIES LTD BALLITO S404"),
            Sale("S1", "DISCHEM", "R1", 150m, new DateOnly(2025, 9, 1), "DIS-CHEM PHARMACIES LTD BALLITO S404"),
            Sale("S1", "DISCHEM", "R1", 999m, new DateOnly(2026, 7, 1), "DIS-CHEM PHARMACIES LTD BALLITO S404"),
        ];

        var sheet = MerchandiserStatementBuilder.Build(company, October, October, sales, []).DisChem!;

        Assert.Equal([new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)], sheet.Months);
        var store = Assert.Single(sheet.Stores);
        Assert.Equal("DIS-CHEM BALLITO S404", store.Name);
        Assert.Equal([100m, 0m, 200m], store.ThisYear);
        Assert.Equal([0m, 150m, 0m], store.LastYear);
        Assert.Equal(100.0m, store.GrowthPercent);
    }

    [Fact]
    public void A_company_without_the_dis_chem_option_has_no_sheet()
    {
        var company = Company(new StatementAreaSetting("Area", null, [Line("Dis-Chem", "DISCHEM", ["R1"], disChem: true)], []));

        Assert.Null(MerchandiserStatementBuilder.Build(company, October, October, [], []).DisChem);
    }
}
