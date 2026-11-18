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
        drop view if exists reporting.{ExcludedLinesView};
        drop view if exists reporting.{ReportLinesView};
        """;
}
