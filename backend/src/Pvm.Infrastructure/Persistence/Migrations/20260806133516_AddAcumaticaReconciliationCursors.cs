using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pvm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAcumaticaReconciliationCursors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SourceLastModifiedAt",
                table: "invoice_candidates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CursorAfter",
                table: "integration_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CursorBefore",
                table: "integration_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "QueryFrom",
                table: "integration_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "QueryTo",
                table: "integration_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_integration_runs_RunType_CursorAfter",
                table: "integration_runs",
                columns: new[] { "RunType", "CursorAfter" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_integration_runs_RunType_CursorAfter",
                table: "integration_runs");

            migrationBuilder.DropColumn(
                name: "SourceLastModifiedAt",
                table: "invoice_candidates");

            migrationBuilder.DropColumn(
                name: "CursorAfter",
                table: "integration_runs");

            migrationBuilder.DropColumn(
                name: "CursorBefore",
                table: "integration_runs");

            migrationBuilder.DropColumn(
                name: "QueryFrom",
                table: "integration_runs");

            migrationBuilder.DropColumn(
                name: "QueryTo",
                table: "integration_runs");
        }
    }
}
