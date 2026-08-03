using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pvm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integration_message_deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QueueName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MessageType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CausationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DeliveryCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ErrorSummary = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    DeadLetterReason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EnqueuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FirstReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_message_deliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "integration_outbox_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QueueName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MessageType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CausationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PublishAttempts = table.Column<int>(type: "integer", nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LastErrorSummary = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_integration_message_deliveries_CorrelationId",
                table: "integration_message_deliveries",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_integration_message_deliveries_QueueName_MessageId",
                table: "integration_message_deliveries",
                columns: new[] { "QueueName", "MessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integration_message_deliveries_Status_UpdatedAt",
                table: "integration_message_deliveries",
                columns: new[] { "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_integration_outbox_messages_CorrelationId",
                table: "integration_outbox_messages",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_integration_outbox_messages_Status_AvailableAt",
                table: "integration_outbox_messages",
                columns: new[] { "Status", "AvailableAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_message_deliveries");

            migrationBuilder.DropTable(
                name: "integration_outbox_messages");
        }
    }
}
