using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pvm.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The monthly summaries of the sell-in report lines, for a fast sales pivot. The model does
    /// not change: the summaries are SQL only. Refer to SellInReportViews.
    /// </summary>
    [DbContext(typeof(PvmDbContext))]
    [Migration("20261120090000_AddSalesReportSummaries")]
    public partial class AddSalesReportSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SellInReportViews.CreateSummaries);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SellInReportViews.DropSummaries);
        }
    }
}
