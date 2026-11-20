namespace Pvm.Infrastructure.Persistence;

/// <summary>
/// The SQL of the sell-in report views. The rules are the rules of the SQL views behind the old
/// sales workbook, found from its data (docs/design/reports-analysis.md, section 7a):
/// <list type="number">
/// <item>A line counts in the month of its posting period. The load sets the month so.</item>
/// <item>Only invoices and credit memos count. Debit memos are left out.</item>
/// <item>Items without an item class are left out.</item>
/// <item>Rep and customer group are the customer's current values.</item>
/// <item>Lines with no rep stay in.</item>
/// </list>
/// A migration that changes a rule drops and makes the views again with the new SQL.
/// </summary>
public static class SellInReportViews
{
    public const string ReportLinesView = "sell_in_report_lines";
    public const string ExcludedLinesView = "sell_in_excluded_lines";

    private const string ExclusionReason = """
        case
            when l."DocumentType" = 'DebitMemo' then 'debit-memo'
            when p."ItemClassId" is null then 'no-item-class'
        end
        """;

    public const string CreateReportLines = $"""
        create view reporting.{ReportLinesView} as
        select
            l."Id",
            l."DocumentType",
            l."ReferenceNumber",
            l."LineNumber",
            l."DocumentDate",
            l."FinancialYear",
            l."FinancialMonth",
            l."PostPeriod",
            l."CustomerId",
            c."Name" as "CustomerName",
            c."CustomerClassId",
            c."CustomerClassName",
            c."Province",
            c."CurrentSalesRepId" as "SalesRepId",
            current_rep."Name" as "SalesRepName",
            l."SalesRepId" as "SalesRepAtSaleId",
            sale_rep."Name" as "SalesRepAtSaleName",
            l."InventoryId",
            p."Description" as "ItemDescription",
            p."ItemClassId",
            p."ItemClassName",
            l."WarehouseId",
            l."LocationId",
            l."Quantity",
            l."BaseQuantity",
            l."Amount"
        from reporting.sell_in_lines l
        left join reporting.customers c on c."CustomerId" = l."CustomerId"
        left join reporting.products p on p."InventoryId" = l."InventoryId"
        left join reporting.sales_reps current_rep on current_rep."SalesRepId" = c."CurrentSalesRepId"
        left join reporting.sales_reps sale_rep on sale_rep."SalesRepId" = l."SalesRepId"
        where ({ExclusionReason}) is null;
        """;

    public const string CreateExcludedLines = $"""
        create view reporting.{ExcludedLinesView} as
        select
            l."Id",
            {ExclusionReason} as "Reason",
            l."DocumentType",
            l."ReferenceNumber",
            l."LineNumber",
            l."FinancialYear",
            l."FinancialMonth",
            l."CustomerId",
            l."InventoryId",
            l."Amount"
        from reporting.sell_in_lines l
        left join reporting.products p on p."InventoryId" = l."InventoryId"
        where ({ExclusionReason}) is not null;
        """;

    public const string Drop = $"""
        {DropSummaries}
        drop view if exists reporting.{ExcludedLinesView};
        drop view if exists reporting.{ReportLinesView};
        """;

    /// <summary>
    /// The monthly summary of the report lines, for the sales pivot. One row for each month,
    /// customer, product, warehouse and rep at the sale, with the keys and the labels that the
    /// pivot groups by, so a query needs no join. The sell-in load refreshes it.
    /// </summary>
    public const string MonthsView = "sell_in_report_months";

    /// <summary>
    /// The monthly summary by customer only: rep, customer group, customer and province. It is
    /// much smaller, and the pivot reads it when a request uses only those dimensions.
    /// </summary>
    public const string CustomerMonthsView = "sell_in_report_customer_months";

    public const string CreateSummaries = $"""
        create materialized view reporting.{MonthsView} as
        select
            l."FinancialYear" * 100 + l."FinancialMonth" as month_key,
            coalesce(l."SalesRepId", '') as rep_key,
            coalesce(l."SalesRepName", l."SalesRepId", 'No rep') as rep_label,
            coalesce(l."SalesRepAtSaleId", '') as rep_at_sale_key,
            coalesce(l."SalesRepAtSaleName", l."SalesRepAtSaleId", 'No rep') as rep_at_sale_label,
            coalesce(l."CustomerClassId", '') as customer_group_key,
            coalesce(l."CustomerClassName", l."CustomerClassId", 'No group') as customer_group_label,
            l."CustomerId" as customer_key,
            coalesce(l."CustomerName", l."CustomerId") as customer_label,
            coalesce(l."Province", '') as province_key,
            coalesce(nullif(l."Province", ''), 'No province') as province_label,
            coalesce(l."ItemClassId", '') as item_group_key,
            coalesce(l."ItemClassName", l."ItemClassId", 'No group') as item_group_label,
            l."InventoryId" as product_key,
            coalesce(l."ItemDescription", l."InventoryId") as product_label,
            coalesce(l."WarehouseId", '') as warehouse_key,
            coalesce(nullif(l."WarehouseId", ''), 'No warehouse') as warehouse_label,
            sum(l."Amount") as amount,
            sum(l."Quantity") as quantity
        from reporting.{ReportLinesView} l
        group by 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17;
        create index ix_{MonthsView}_month on reporting.{MonthsView} (month_key);

        create materialized view reporting.{CustomerMonthsView} as
        select month_key, rep_key, rep_label, customer_group_key, customer_group_label, customer_key, customer_label,
            province_key, province_label, sum(amount) as amount, sum(quantity) as quantity
        from reporting.{MonthsView}
        group by 1, 2, 3, 4, 5, 6, 7, 8, 9;
        create index ix_{CustomerMonthsView}_month on reporting.{CustomerMonthsView} (month_key);
        """;

    public const string DropSummaries = $"""
        drop materialized view if exists reporting.{CustomerMonthsView};
        drop materialized view if exists reporting.{MonthsView};
        """;

    /// <summary>Refreshes the summaries after a load. The customer summary reads the month summary, so it comes second.</summary>
    public const string RefreshSummaries = $"""
        refresh materialized view reporting.{MonthsView};
        refresh materialized view reporting.{CustomerMonthsView};
        """;
}
