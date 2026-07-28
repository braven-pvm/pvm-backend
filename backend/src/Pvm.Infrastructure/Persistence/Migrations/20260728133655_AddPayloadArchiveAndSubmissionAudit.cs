using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pvm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayloadArchiveAndSubmissionAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RequestPayload",
                table: "submission_operations",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "FrozenCanonicalJson",
                table: "submission_operations",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.CreateTable(
                name: "payload_archives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceCandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Location = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Sha256Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ByteCount = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payload_archives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payload_archives_invoice_candidates_InvoiceCandidateId",
                        column: x => x.InvoiceCandidateId,
                        principalTable: "invoice_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payload_archives_submission_operations_SubmissionOperationId",
                        column: x => x.SubmissionOperationId,
                        principalTable: "submission_operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "submission_operation_transitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceCandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Actor = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Mode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreviousState = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    NewState = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SourceVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submission_operation_transitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_submission_operation_transitions_invoice_candidates_Invoice~",
                        column: x => x.InvoiceCandidateId,
                        principalTable: "invoice_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_submission_operation_transitions_submission_operations_Subm~",
                        column: x => x.SubmissionOperationId,
                        principalTable: "submission_operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payload_archives_InvoiceCandidateId",
                table: "payload_archives",
                column: "InvoiceCandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_payload_archives_SubmissionOperationId_Kind",
                table: "payload_archives",
                columns: new[] { "SubmissionOperationId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_submission_operation_transitions_InvoiceCandidateId_Created~",
                table: "submission_operation_transitions",
                columns: new[] { "InvoiceCandidateId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_submission_operation_transitions_SubmissionOperationId_Crea~",
                table: "submission_operation_transitions",
                columns: new[] { "SubmissionOperationId", "CreatedAt" });

            migrationBuilder.Sql(
                """
                create function reject_immutable_evidence_mutation()
                returns trigger
                language plpgsql
                as $$
                begin
                    raise exception 'Immutable evidence records cannot be updated or deleted.'
                        using errcode = '55000';
                end;
                $$;

                create trigger payload_archives_are_immutable
                before update or delete on payload_archives
                for each row execute function reject_immutable_evidence_mutation();

                create trigger submission_operation_transitions_are_immutable
                before update or delete on submission_operation_transitions
                for each row execute function reject_immutable_evidence_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payload_archives");

            migrationBuilder.DropTable(
                name: "submission_operation_transitions");

            migrationBuilder.Sql("drop function reject_immutable_evidence_mutation();");

            migrationBuilder.AlterColumn<string>(
                name: "RequestPayload",
                table: "submission_operations",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FrozenCanonicalJson",
                table: "submission_operations",
                type: "jsonb",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);
        }
    }
}
