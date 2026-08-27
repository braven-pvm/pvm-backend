using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pvm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShopriteOrderAcknowledgement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AcknowledgedAt",
                table: "shoprite_purchase_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AcknowledgementAttempts",
                table: "shoprite_purchase_orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastAcknowledgementError",
                table: "shoprite_purchase_orders",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_shoprite_purchase_orders_AcknowledgedAt",
                table: "shoprite_purchase_orders",
                column: "AcknowledgedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_shoprite_purchase_orders_AcknowledgedAt",
                table: "shoprite_purchase_orders");

            migrationBuilder.DropColumn(
                name: "AcknowledgedAt",
                table: "shoprite_purchase_orders");

            migrationBuilder.DropColumn(
                name: "AcknowledgementAttempts",
                table: "shoprite_purchase_orders");

            migrationBuilder.DropColumn(
                name: "LastAcknowledgementError",
                table: "shoprite_purchase_orders");
        }
    }
}
