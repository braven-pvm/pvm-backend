using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pvm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReportingSellInModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reporting");

            migrationBuilder.CreateTable(
                name: "customer_class_channels",
                schema: "reporting",
                columns: table => new
                {
                    CustomerClassId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_class_channels", x => x.CustomerClassId);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "reporting",
                columns: table => new
                {
                    CustomerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CustomerClassId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CustomerClassName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Province = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.CustomerId);
                });

            migrationBuilder.CreateTable(
                name: "products",
                schema: "reporting",
                columns: table => new
                {
                    InventoryId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ItemClassId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ItemClassName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.InventoryId);
                });

            migrationBuilder.CreateTable(
                name: "sales_reps",
                schema: "reporting",
                columns: table => new
                {
                    SalesRepId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_reps", x => x.SalesRepId);
                });

            migrationBuilder.CreateTable(
                name: "sell_in_lines",
                schema: "reporting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ReferenceNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    DocumentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FinancialYear = table.Column<int>(type: "integer", nullable: false),
                    FinancialMonth = table.Column<int>(type: "integer", nullable: false),
                    PostPeriod = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    CustomerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SalesRepId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    InventoryId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WarehouseId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LocationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    BaseQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AcumaticaLastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LoadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sell_in_lines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "warehouse_locations",
                schema: "reporting",
                columns: table => new
                {
                    WarehouseId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LocationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WarehouseName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LocationName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_warehouse_locations", x => new { x.WarehouseId, x.LocationId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_customers_CustomerClassId",
                schema: "reporting",
                table: "customers",
                column: "CustomerClassId");

            migrationBuilder.CreateIndex(
                name: "IX_sell_in_lines_CustomerId",
                schema: "reporting",
                table: "sell_in_lines",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_sell_in_lines_DocumentType_ReferenceNumber_LineNumber",
                schema: "reporting",
                table: "sell_in_lines",
                columns: new[] { "DocumentType", "ReferenceNumber", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sell_in_lines_FinancialYear_FinancialMonth",
                schema: "reporting",
                table: "sell_in_lines",
                columns: new[] { "FinancialYear", "FinancialMonth" });

            migrationBuilder.CreateIndex(
                name: "IX_sell_in_lines_InventoryId",
                schema: "reporting",
                table: "sell_in_lines",
                column: "InventoryId");

            migrationBuilder.CreateIndex(
                name: "IX_sell_in_lines_SalesRepId",
                schema: "reporting",
                table: "sell_in_lines",
                column: "SalesRepId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_class_channels",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "products",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "sales_reps",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "sell_in_lines",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "warehouse_locations",
                schema: "reporting");
        }
    }
}
