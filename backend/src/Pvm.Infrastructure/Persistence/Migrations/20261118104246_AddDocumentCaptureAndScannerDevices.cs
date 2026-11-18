using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pvm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentCaptureAndScannerDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scanner_devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    KeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EnrolledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scanner_devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "scanner_staff",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scanner_staff", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "scanner_device_setup_codes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scanner_device_setup_codes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_scanner_device_setup_codes_scanner_devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "scanner_devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                    ScannerDeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScannerStaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScannerStaffName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
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
                    table.ForeignKey(
                        name: "FK_captured_documents_scanner_devices_ScannerDeviceId",
                        column: x => x.ScannerDeviceId,
                        principalTable: "scanner_devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_captured_documents_scanner_staff_ScannerStaffId",
                        column: x => x.ScannerStaffId,
                        principalTable: "scanner_staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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

            migrationBuilder.CreateIndex(
                name: "IX_captured_documents_ScannerDeviceId",
                table: "captured_documents",
                column: "ScannerDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_captured_documents_ScannerStaffId",
                table: "captured_documents",
                column: "ScannerStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_scanner_device_setup_codes_CodeHash",
                table: "scanner_device_setup_codes",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_scanner_device_setup_codes_DeviceId",
                table: "scanner_device_setup_codes",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_scanner_devices_KeyHash",
                table: "scanner_devices",
                column: "KeyHash",
                unique: true);

            // One active device per name, ignoring case. EF cannot model an index on an
            // expression, so it is SQL here and not in the model snapshot.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_scanner_devices_active_name_lower ON scanner_devices (lower(\"Name\")) WHERE \"RevokedAt\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_scanner_devices_active_name_lower;");

            migrationBuilder.DropTable(
                name: "captured_documents");

            migrationBuilder.DropTable(
                name: "scanner_device_setup_codes");

            migrationBuilder.DropTable(
                name: "scanner_staff");

            migrationBuilder.DropTable(
                name: "scanner_devices");
        }
    }
}
