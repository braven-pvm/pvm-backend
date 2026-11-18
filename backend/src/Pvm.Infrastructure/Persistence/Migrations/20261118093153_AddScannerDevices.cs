using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pvm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScannerDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ScannerDeviceId",
                table: "captured_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ScannerStaffId",
                table: "captured_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScannerStaffName",
                table: "captured_documents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_scanner_devices_Name",
                table: "scanner_devices",
                column: "Name",
                unique: true,
                filter: "\"RevokedAt\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_captured_documents_scanner_devices_ScannerDeviceId",
                table: "captured_documents",
                column: "ScannerDeviceId",
                principalTable: "scanner_devices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_captured_documents_scanner_staff_ScannerStaffId",
                table: "captured_documents",
                column: "ScannerStaffId",
                principalTable: "scanner_staff",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_captured_documents_scanner_devices_ScannerDeviceId",
                table: "captured_documents");

            migrationBuilder.DropForeignKey(
                name: "FK_captured_documents_scanner_staff_ScannerStaffId",
                table: "captured_documents");

            migrationBuilder.DropTable(
                name: "scanner_device_setup_codes");

            migrationBuilder.DropTable(
                name: "scanner_staff");

            migrationBuilder.DropTable(
                name: "scanner_devices");

            migrationBuilder.DropIndex(
                name: "IX_captured_documents_ScannerDeviceId",
                table: "captured_documents");

            migrationBuilder.DropIndex(
                name: "IX_captured_documents_ScannerStaffId",
                table: "captured_documents");

            migrationBuilder.DropColumn(
                name: "ScannerDeviceId",
                table: "captured_documents");

            migrationBuilder.DropColumn(
                name: "ScannerStaffId",
                table: "captured_documents");

            migrationBuilder.DropColumn(
                name: "ScannerStaffName",
                table: "captured_documents");
        }
    }
}
