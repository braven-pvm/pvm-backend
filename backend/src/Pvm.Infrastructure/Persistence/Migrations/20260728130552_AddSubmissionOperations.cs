using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pvm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubmissionOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SubmissionOperationId",
                table: "invoice_submission_attempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "submission_operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceCandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommandId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InitiatedBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    InitiationMode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FrozenSourceJson = table.Column<string>(type: "jsonb", nullable: true),
                    FrozenCanonicalJson = table.Column<string>(type: "jsonb", nullable: false),
                    RequestPayload = table.Column<string>(type: "text", nullable: false),
                    RequestPayloadHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResponseStatusCode = table.Column<int>(type: "integer", nullable: true),
                    ResponsePayload = table.Column<string>(type: "text", nullable: true),
                    ResponsePayloadHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    FailureClassification = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SendingStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submission_operations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_submission_operations_invoice_candidates_InvoiceCandidateId",
                        column: x => x.InvoiceCandidateId,
                        principalTable: "invoice_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_invoice_submission_attempts_SubmissionOperationId",
                table: "invoice_submission_attempts",
                column: "SubmissionOperationId",
                unique: true,
                filter: "\"SubmissionOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_submission_operations_CommandId",
                table: "submission_operations",
                column: "CommandId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_submission_operations_InvoiceCandidateId",
                table: "submission_operations",
                column: "InvoiceCandidateId",
                unique: true,
                filter: "\"Status\" IN ('Pending', 'Sending', 'Submitted', 'Ambiguous')");

            migrationBuilder.CreateIndex(
                name: "IX_submission_operations_InvoiceCandidateId_Generation",
                table: "submission_operations",
                columns: new[] { "InvoiceCandidateId", "Generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_submission_operations_Status_SendingStartedAt",
                table: "submission_operations",
                columns: new[] { "Status", "SendingStartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "submission_operations");

            migrationBuilder.DropIndex(
                name: "IX_invoice_submission_attempts_SubmissionOperationId",
                table: "invoice_submission_attempts");

            migrationBuilder.DropColumn(
                name: "SubmissionOperationId",
                table: "invoice_submission_attempts");
        }
    }
}
