using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pvm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMerchandiserSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "merchandiser_companies",
                schema: "reporting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StatementTitle = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PaymentKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PaymentRatePercent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: true),
                    RetainerAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ShowDisChemYearOnYear = table.Column<bool>(type: "boolean", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_merchandiser_companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "merchandiser_areas",
                schema: "reporting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    CommissionRatePercent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_merchandiser_areas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_merchandiser_areas_merchandiser_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "reporting",
                        principalTable: "merchandiser_companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "merchandiser_lines",
                schema: "reporting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AreaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    CustomerClassId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SalesRepIds = table.Column<List<string>>(type: "text[]", nullable: false),
                    CustomerIds = table.Column<List<string>>(type: "text[]", nullable: false),
                    IsDisChemLine = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_merchandiser_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_merchandiser_lines_merchandiser_areas_AreaId",
                        column: x => x.AreaId,
                        principalSchema: "reporting",
                        principalTable: "merchandiser_areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "merchandiser_makro_stores",
                schema: "reporting",
                columns: table => new
                {
                    AreaId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_merchandiser_makro_stores", x => new { x.AreaId, x.StoreId });
                    table.ForeignKey(
                        name: "FK_merchandiser_makro_stores_merchandiser_areas_AreaId",
                        column: x => x.AreaId,
                        principalSchema: "reporting",
                        principalTable: "merchandiser_areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_merchandiser_makro_stores_retail_stores_StoreId",
                        column: x => x.StoreId,
                        principalSchema: "reporting",
                        principalTable: "retail_stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_merchandiser_areas_CompanyId_Order",
                schema: "reporting",
                table: "merchandiser_areas",
                columns: new[] { "CompanyId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_merchandiser_companies_Name_ValidFrom",
                schema: "reporting",
                table: "merchandiser_companies",
                columns: new[] { "Name", "ValidFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_merchandiser_lines_AreaId_Order",
                schema: "reporting",
                table: "merchandiser_lines",
                columns: new[] { "AreaId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_merchandiser_makro_stores_StoreId",
                schema: "reporting",
                table: "merchandiser_makro_stores",
                column: "StoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "merchandiser_lines",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "merchandiser_makro_stores",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "merchandiser_areas",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "merchandiser_companies",
                schema: "reporting");
        }
    }
}
