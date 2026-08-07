using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pvm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAcumaticaPushNotificationInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integration_event_inbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceEnvironment = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CompanyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    QueryName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationTimestamp = table.Column<long>(type: "bigint", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InsertedCount = table.Column<int>(type: "integer", nullable: false),
                    DeletedCount = table.Column<int>(type: "integer", nullable: false),
                    EnqueuedCount = table.Column<int>(type: "integer", nullable: false),
                    DuplicateCount = table.Column<int>(type: "integer", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_event_inbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_integration_event_inbox_ReceivedAt",
                table: "integration_event_inbox",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_integration_event_inbox_SourceEnvironment_CompanyId_QueryNa~",
                table: "integration_event_inbox",
                columns: new[] { "SourceEnvironment", "CompanyId", "QueryName", "TransactionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_event_inbox");
        }
    }
}
