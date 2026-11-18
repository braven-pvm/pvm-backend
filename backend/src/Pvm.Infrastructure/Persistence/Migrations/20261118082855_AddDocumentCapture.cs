using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pvm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "captured_documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReviewReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BlobPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    PageCount = table.Column<int>(type: "integer", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExtractionJson = table.Column<string>(type: "jsonb", nullable: true),
                    MatchedReference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MatchedCustomerName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    MatchedBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    AcumaticaAttachPending = table.Column<bool>(type: "boolean", nullable: false),
                    AcumaticaAttachedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcumaticaAttachLastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UploadedBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewNote = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    RejectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RejectedBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_captured_documents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_captured_documents_ClientDocumentId",
                table: "captured_documents",
                column: "ClientDocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_captured_documents_MatchedReference",
                table: "captured_documents",
                column: "MatchedReference");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "captured_documents");
        }
    }
}
