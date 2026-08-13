using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pvm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationPolicyAndDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "automation_decisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceCandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyVersion = table.Column<int>(type: "integer", nullable: false),
                    SourceVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReasonCodes = table.Column<string[]>(type: "text[]", nullable: false),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    NotBefore = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CommandId = table.Column<Guid>(type: "uuid", nullable: true),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    EvaluatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_decisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_automation_decisions_invoice_candidates_InvoiceCandidateId",
                        column: x => x.InvoiceCandidateId,
                        principalTable: "invoice_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "automation_policy_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EmergencyStop = table.Column<bool>(type: "boolean", nullable: false),
                    AccountAllowlist = table.Column<string[]>(type: "text[]", nullable: false),
                    LocationAllowlist = table.Column<string[]>(type: "text[]", nullable: false),
                    SupportedOrderTypes = table.Column<string[]>(type: "text[]", nullable: false),
                    StabilizationDelayMinutes = table.Column<int>(type: "integer", nullable: false),
                    PurchaseOrderFreshnessMinutes = table.Column<int>(type: "integer", nullable: false),
                    AcumaticaFreshnessMinutes = table.Column<int>(type: "integer", nullable: false),
                    DailyAutomaticSubmissionCap = table.Column<int>(type: "integer", nullable: false),
                    AutomaticWindowStart = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    AutomaticWindowEnd = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_policy_versions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_automation_decisions_CommandId",
                table: "automation_decisions",
                column: "CommandId",
                unique: true,
                filter: "\"CommandId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_automation_decisions_EvaluatedAt",
                table: "automation_decisions",
                column: "EvaluatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_automation_decisions_InvoiceCandidateId_PolicyVersion_Sourc~",
                table: "automation_decisions",
                columns: new[] { "InvoiceCandidateId", "PolicyVersion", "SourceVersion", "Outcome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_automation_policy_versions_Version",
                table: "automation_policy_versions",
                column: "Version",
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO automation_policy_versions
                    ("Id", "Version", "Mode", "EmergencyStop", "AccountAllowlist",
                     "LocationAllowlist", "SupportedOrderTypes", "StabilizationDelayMinutes",
                     "PurchaseOrderFreshnessMinutes", "AcumaticaFreshnessMinutes",
                     "DailyAutomaticSubmissionCap", "AutomaticWindowStart", "AutomaticWindowEnd",
                     "TimeZoneId", "CreatedBy", "Reason", "CreatedAt")
                VALUES
                    ('4f2f1e9b-f321-4aba-924d-2ba9bddc5cd0', 1, 'Disabled', FALSE,
                     ARRAY[]::text[], ARRAY[]::text[], ARRAY['220']::text[], 15, 15, 30, 10,
                     TIME '06:00', TIME '18:00', 'Africa/Johannesburg',
                     'system:migration', 'Initial safe default with automatic submission disabled.', NOW());

                INSERT INTO audit_events
                    ("Id", "EntityType", "EntityId", "Action", "Actor", "DetailsJson", "CreatedAt")
                VALUES
                    ('91409bb0-627c-437e-8b8d-2ccdd1194599', 'AutomationPolicy', '1',
                     'automation-policy-initialized', 'system:migration',
                     '{"mode":"Disabled","emergencyStop":false,"reason":"Initial safe default with automatic submission disabled."}'::jsonb,
                     NOW());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "automation_decisions");

            migrationBuilder.DropTable(
                name: "automation_policy_versions");
        }
    }
}
