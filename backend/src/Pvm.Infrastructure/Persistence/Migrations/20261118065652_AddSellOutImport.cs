using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pvm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSellOutImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_gtins",
                schema: "reporting",
                columns: table => new
                {
                    Gtin = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    InventoryId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_gtins", x => x.Gtin);
                });

            migrationBuilder.CreateTable(
                name: "retail_stores",
                schema: "reporting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Retailer = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StoreCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Brand = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Region = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Province = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Gln = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    AcumaticaCustomerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_retail_stores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sell_out_imports",
                schema: "reporting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    FileSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    FileDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReceivedBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FirstMonth = table.Column<DateOnly>(type: "date", nullable: true),
                    LastMonth = table.Column<DateOnly>(type: "date", nullable: true),
                    RowsRead = table.Column<int>(type: "integer", nullable: false),
                    RowsLoaded = table.Column<int>(type: "integer", nullable: false),
                    RowsUnmapped = table.Column<int>(type: "integer", nullable: false),
                    RowsRefused = table.Column<int>(type: "integer", nullable: false),
                    StatusReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ArchivePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LoadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sell_out_imports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sell_out_product_maps",
                schema: "reporting",
                columns: table => new
                {
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceProductCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InventoryId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UnitsPerEach = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MappedBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    MappedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sell_out_product_maps", x => new { x.Source, x.SourceProductCode });
                });

            migrationBuilder.CreateTable(
                name: "sell_out_months",
                schema: "reporting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SourceProductCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    FinancialYear = table.Column<int>(type: "integer", nullable: false),
                    FinancialMonth = table.Column<int>(type: "integer", nullable: false),
                    Units = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitsEach = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ValueExclVat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    IsPartial = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sell_out_months", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sell_out_months_retail_stores_StoreId",
                        column: x => x.StoreId,
                        principalSchema: "reporting",
                        principalTable: "retail_stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "store_stock_snapshots",
                schema: "reporting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SourceProductCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StockOnHand = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    StockOnOrder = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    StockInTransit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    StockValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    WeeksOfStock = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    DcStock = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_stock_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_store_stock_snapshots_retail_stores_StoreId",
                        column: x => x.StoreId,
                        principalSchema: "reporting",
                        principalTable: "retail_stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sell_out_import_issues",
                schema: "reporting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RawKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sell_out_import_issues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sell_out_import_issues_sell_out_imports_ImportId",
                        column: x => x.ImportId,
                        principalSchema: "reporting",
                        principalTable: "sell_out_imports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_gtins_InventoryId",
                schema: "reporting",
                table: "product_gtins",
                column: "InventoryId");

            migrationBuilder.CreateIndex(
                name: "IX_retail_stores_Retailer_StoreCode",
                schema: "reporting",
                table: "retail_stores",
                columns: new[] { "Retailer", "StoreCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sell_out_import_issues_ImportId",
                schema: "reporting",
                table: "sell_out_import_issues",
                column: "ImportId");

            migrationBuilder.CreateIndex(
                name: "IX_sell_out_imports_FileSha256",
                schema: "reporting",
                table: "sell_out_imports",
                column: "FileSha256",
                unique: true,
                filter: "\"Status\" = 'Loaded'");

            migrationBuilder.CreateIndex(
                name: "IX_sell_out_imports_Source_CreatedAt",
                schema: "reporting",
                table: "sell_out_imports",
                columns: new[] { "Source", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sell_out_months_FinancialYear_FinancialMonth",
                schema: "reporting",
                table: "sell_out_months",
                columns: new[] { "FinancialYear", "FinancialMonth" });

            migrationBuilder.CreateIndex(
                name: "IX_sell_out_months_ImportId",
                schema: "reporting",
                table: "sell_out_months",
                column: "ImportId");

            migrationBuilder.CreateIndex(
                name: "IX_sell_out_months_InventoryId",
                schema: "reporting",
                table: "sell_out_months",
                column: "InventoryId");

            migrationBuilder.CreateIndex(
                name: "IX_sell_out_months_Source_StoreId_SourceProductCode_Year_Month",
                schema: "reporting",
                table: "sell_out_months",
                columns: new[] { "Source", "StoreId", "SourceProductCode", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sell_out_months_Source_Year_Month",
                schema: "reporting",
                table: "sell_out_months",
                columns: new[] { "Source", "Year", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_sell_out_months_StoreId",
                schema: "reporting",
                table: "sell_out_months",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_store_stock_snapshots_ImportId",
                schema: "reporting",
                table: "store_stock_snapshots",
                column: "ImportId");

            migrationBuilder.CreateIndex(
                name: "IX_store_stock_snapshots_Source_StoreId_SourceProductCode_Snap~",
                schema: "reporting",
                table: "store_stock_snapshots",
                columns: new[] { "Source", "StoreId", "SourceProductCode", "SnapshotDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_store_stock_snapshots_StoreId",
                schema: "reporting",
                table: "store_stock_snapshots",
                column: "StoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_gtins",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "sell_out_import_issues",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "sell_out_months",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "sell_out_product_maps",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "store_stock_snapshots",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "sell_out_imports",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "retail_stores",
                schema: "reporting");
        }
    }
}
