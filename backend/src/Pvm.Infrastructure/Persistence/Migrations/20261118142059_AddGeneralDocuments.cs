using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pvm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGeneralDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AutoTitle",
                table: "captured_documents",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DocumentDate",
                table: "captured_documents",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "captured_documents",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "captured_documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "SearchWords",
                table: "captured_documents",
                type: "text[]",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "captured_documents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailBlobPath",
                table: "captured_documents",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "captured_documents",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_captured_documents_DocumentType_UploadedAt",
                table: "captured_documents",
                columns: new[] { "DocumentType", "UploadedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_captured_documents_DocumentType_UploadedAt",
                table: "captured_documents");

            migrationBuilder.DropColumn(
                name: "AutoTitle",
                table: "captured_documents");

            migrationBuilder.DropColumn(
                name: "DocumentDate",
                table: "captured_documents");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "captured_documents");

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "captured_documents");

            migrationBuilder.DropColumn(
                name: "SearchWords",
                table: "captured_documents");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "captured_documents");

            migrationBuilder.DropColumn(
                name: "ThumbnailBlobPath",
                table: "captured_documents");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "captured_documents");
        }
    }
}
