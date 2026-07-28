using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pvm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntraObjectId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Actor = table.Column<string>(type: "text", nullable: false),
                    DetailsJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "shoprite_item_mappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AcumaticaInventoryId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ShopriteBuyerItemId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Gtin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shoprite_item_mappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "shoprite_purchase_orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OrderHeaderId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OrderTypeCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    OrderTypeLabel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SupplierGln = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    BuyerGln = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    DeliveryGln = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    DeliveryLocationCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DeliveryLocationName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DeliveryLocationSource = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    TotalExcludingTax = table.Column<decimal>(type: "numeric", nullable: true),
                    TotalIncludingTax = table.Column<decimal>(type: "numeric", nullable: true),
                    TotalTax = table.Column<decimal>(type: "numeric", nullable: true),
                    SourceEnvironment = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceEndpoint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RawOrderJson = table.Column<string>(type: "jsonb", nullable: true),
                    ShopriteCreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ShopriteLastUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shoprite_purchase_orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "shoprite_uom_mappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AcumaticaInventoryId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AcumaticaUom = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ShopriteUom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shoprite_uom_mappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "app_user_audit_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorAppUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetAppUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: true),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_user_audit_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_app_user_audit_events_app_users_ActorAppUserId",
                        column: x => x.ActorAppUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_app_user_audit_events_app_users_TargetAppUserId",
                        column: x => x.TargetAppUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "app_user_roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GrantedByAppUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_user_roles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_app_user_roles_app_users_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_app_user_roles_app_users_GrantedByAppUserId",
                        column: x => x.GrantedByAppUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "invoice_candidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AcumaticaInvoiceId = table.Column<string>(type: "text", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "text", nullable: false),
                    CustomerAccount = table.Column<string>(type: "text", nullable: false),
                    CustomerLocation = table.Column<string>(type: "text", nullable: true),
                    ShopritePurchaseOrderNumber = table.Column<string>(type: "text", nullable: true),
                    MatchedShopritePurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierGln = table.Column<string>(type: "text", nullable: true),
                    StoreDcGln = table.Column<string>(type: "text", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceJson = table.Column<string>(type: "jsonb", nullable: true),
                    CanonicalJson = table.Column<string>(type: "jsonb", nullable: true),
                    ValidationJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_candidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_invoice_candidates_shoprite_purchase_orders_MatchedShoprite~",
                        column: x => x.MatchedShopritePurchaseOrderId,
                        principalTable: "shoprite_purchase_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "shoprite_purchase_order_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopritePurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    Gtin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    BuyerItemId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    BuyerItemDescription = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SupplierItemId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    RequestedQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                    MeasurementUnitCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    NetAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    NetPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    MonetaryAmountExcludingTaxes = table.Column<decimal>(type: "numeric", nullable: true),
                    MonetaryAmountIncludingTaxes = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shoprite_purchase_order_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shoprite_purchase_order_lines_shoprite_purchase_orders_Shop~",
                        column: x => x.ShopritePurchaseOrderId,
                        principalTable: "shoprite_purchase_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invoice_submission_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceCandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    InitiatedBy = table.Column<string>(type: "text", nullable: false),
                    InitiationMode = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestPayload = table.Column<string>(type: "text", nullable: true),
                    RequestPayloadLocation = table.Column<string>(type: "text", nullable: true),
                    RequestPayloadHash = table.Column<string>(type: "text", nullable: true),
                    ResponseStatusCode = table.Column<int>(type: "integer", nullable: true),
                    ResponsePayload = table.Column<string>(type: "text", nullable: true),
                    ResponsePayloadLocation = table.Column<string>(type: "text", nullable: true),
                    ResponsePayloadHash = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    FailureClassification = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RecommendedFixLocation = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsRetryEligible = table.Column<bool>(type: "boolean", nullable: true),
                    ResponsibleRole = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_submission_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_invoice_submission_attempts_invoice_candidates_InvoiceCandi~",
                        column: x => x.InvoiceCandidateId,
                        principalTable: "invoice_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_app_user_audit_events_ActorAppUserId",
                table: "app_user_audit_events",
                column: "ActorAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_app_user_audit_events_TargetAppUserId",
                table: "app_user_audit_events",
                column: "TargetAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_app_user_roles_AppUserId_Role",
                table: "app_user_roles",
                columns: new[] { "AppUserId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_app_user_roles_GrantedByAppUserId",
                table: "app_user_roles",
                column: "GrantedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_app_users_Email",
                table: "app_users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_app_users_EntraObjectId",
                table: "app_users",
                column: "EntraObjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_EntityType_EntityId",
                table: "audit_events",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_invoice_candidates_IdempotencyKey",
                table: "invoice_candidates",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoice_candidates_MatchedShopritePurchaseOrderId",
                table: "invoice_candidates",
                column: "MatchedShopritePurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_submission_attempts_InvoiceCandidateId",
                table: "invoice_submission_attempts",
                column: "InvoiceCandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_shoprite_item_mappings_AcumaticaInventoryId_ShopriteBuyerIt~",
                table: "shoprite_item_mappings",
                columns: new[] { "AcumaticaInventoryId", "ShopriteBuyerItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shoprite_purchase_order_lines_Gtin",
                table: "shoprite_purchase_order_lines",
                column: "Gtin");

            migrationBuilder.CreateIndex(
                name: "IX_shoprite_purchase_order_lines_ShopritePurchaseOrderId_LineN~",
                table: "shoprite_purchase_order_lines",
                columns: new[] { "ShopritePurchaseOrderId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shoprite_purchase_orders_DeliveryGln",
                table: "shoprite_purchase_orders",
                column: "DeliveryGln");

            migrationBuilder.CreateIndex(
                name: "IX_shoprite_purchase_orders_PurchaseOrderNumber",
                table: "shoprite_purchase_orders",
                column: "PurchaseOrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shoprite_uom_mappings_AcumaticaInventoryId_AcumaticaUom",
                table: "shoprite_uom_mappings",
                columns: new[] { "AcumaticaInventoryId", "AcumaticaUom" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_user_audit_events");

            migrationBuilder.DropTable(
                name: "app_user_roles");

            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "invoice_submission_attempts");

            migrationBuilder.DropTable(
                name: "shoprite_item_mappings");

            migrationBuilder.DropTable(
                name: "shoprite_purchase_order_lines");

            migrationBuilder.DropTable(
                name: "shoprite_uom_mappings");

            migrationBuilder.DropTable(
                name: "app_users");

            migrationBuilder.DropTable(
                name: "invoice_candidates");

            migrationBuilder.DropTable(
                name: "shoprite_purchase_orders");
        }
    }
}
