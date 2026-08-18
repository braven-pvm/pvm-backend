using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pvm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExceptionTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exception_tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeduplicationKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    InvoiceCandidateId = table.Column<Guid>(type: "uuid", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Summary = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    FixLocation = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RetryClassification = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Owner = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    OccurrenceCount = table.Column<int>(type: "integer", nullable: false),
                    LatestEvidence = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: true),
                    IsDerived = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    ResolutionReason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exception_tasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "exception_task_comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExceptionTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    Actor = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Body = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exception_task_comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_exception_task_comments_exception_tasks_ExceptionTaskId",
                        column: x => x.ExceptionTaskId,
                        principalTable: "exception_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_exception_task_comments_ExceptionTaskId",
                table: "exception_task_comments",
                column: "ExceptionTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_exception_tasks_DeduplicationKey",
                table: "exception_tasks",
                column: "DeduplicationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exception_tasks_InvoiceCandidateId",
                table: "exception_tasks",
                column: "InvoiceCandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_exception_tasks_Status_Severity",
                table: "exception_tasks",
                columns: new[] { "Status", "Severity" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exception_task_comments");

            migrationBuilder.DropTable(
                name: "exception_tasks");
        }
    }
}
