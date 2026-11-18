using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pvm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSellInWorkbookRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrentSalesRepId",
                schema: "reporting",
                table: "customers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // Lines loaded before this change have the month of the document date. The month is
            // now the posting period, as in the sales workbook.
            migrationBuilder.Sql("""
                update reporting.sell_in_lines
                set "FinancialYear" = cast(substr("PostPeriod", 1, 4) as integer),
                    "FinancialMonth" = cast(substr("PostPeriod", 5, 2) as integer)
                where "PostPeriod" ~ '^[0-9]{6}$';
                """);

            migrationBuilder.Sql(SellInReportViews.CreateReportLines);
            migrationBuilder.Sql(SellInReportViews.CreateExcludedLines);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SellInReportViews.Drop);

            migrationBuilder.DropColumn(
                name: "CurrentSalesRepId",
                schema: "reporting",
                table: "customers");
        }
    }
}
