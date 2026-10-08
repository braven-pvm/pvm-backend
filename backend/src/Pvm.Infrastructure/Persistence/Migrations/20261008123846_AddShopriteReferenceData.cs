using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pvm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShopriteReferenceData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shoprite_delivery_locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Gln = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LocationType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shoprite_delivery_locations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "shoprite_trade_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AcumaticaInventoryId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AcumaticaUom = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DeliversTo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Gtin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AcumaticaUnitsPerShopriteUnit = table.Column<int>(type: "integer", nullable: false),
                    ShopritePackSize = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ShopriteUom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shoprite_trade_items", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shoprite_delivery_locations_BranchCode",
                table: "shoprite_delivery_locations",
                column: "BranchCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shoprite_trade_items_AcumaticaInventoryId_AcumaticaUom_Deli~",
                table: "shoprite_trade_items",
                columns: new[] { "AcumaticaInventoryId", "AcumaticaUom", "DeliversTo" },
                unique: true);

            // Reference data derived on 2026-10-08 from 131 Shoprite orders already held, each matched
            // to its Acumatica sales order. Every GLN and every store GTIN was observed directly.
            // Distribution-centre case GTINs seen on fewer than three orders load unverified, and an
            // invoice that needs one stays blocked until a person confirms it.
            migrationBuilder.Sql("""
INSERT INTO shoprite_delivery_locations ("Id", "BranchCode", "Gln", "Name", "LocationType", "IsVerified", "UpdatedBy", "CreatedAt", "UpdatedAt") VALUES
(gen_random_uuid(), '1331', '6001001013307', 'CHECKERS METLIFE PLAZA', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '1381', '6001001013802', 'CHECKERS NAHOON', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '1810', '6001001018104', 'CHECKERS 6TH AVENUE', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '2052', '6001001020503', 'CHECKERS OUDTSHOORN', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '30148', '6001001301404', 'CHECKERS VINCENT', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '30562', '6001001305600', 'CHECKERS LORRAINE', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '36021', '6001001360203', 'DC CENTURION', 'DistributionCentre', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '36102', '6001001361002', 'DC CANELANDS', 'DistributionCentre', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '37768', '6001001377607', 'CHECKERS HEMINGWAYS MALL', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '39948', '6001001399401', 'DC BASSON', 'DistributionCentre', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '41074', '6001001410700', 'CHECKERS JEFFREY''S BAY', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '44739', '6001001447300', 'CHECKERS NEWTON PARK', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '48979', '6001001489706', 'CHECKERS KNYSNA', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '50908', '6001001509008', 'CHECKERS FX MOFFETT-ON-MAIN', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '51297', '6001001512909', 'CHECKERS BEAUFORT WEST', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '52073', '6001001520706', 'CHECKERS JEFFREY''S BAY MALL', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '57073', '6001001570701', 'CHECKERS FX YORK ST CENTRE', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '6129', '6001001061209', 'CHECKERS ST GEORGES SQUARE', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '6250', '6001001062503', 'CHECKERS GRAHAMSTOWN', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '6268', '6001001062602', 'CHECKERS GREENACRES', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '6331', '6001001063302', 'CHECKERS HEIDERAND', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '64646', '6001001646406', 'CHECKERS BAYSIDE (MOSSEL BAY)', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '80460', '6001001804608', 'CHECKERS HYPER BAY WEST', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '82917', '6001001829106', 'CHECKERS FX PLETTENBERG BAY MALL', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '84082', '6001001840804', 'CHECKERS CORKWOOD SQUARE', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '91867', '6001001918602', 'CHECKERS HYPER EDEN MEANDER', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), '96249', '6001001962407', 'CHECKERS FX BOARDWALK PE', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), 'G191', '6001001619196', 'DC RIVERFIELDS', 'DistributionCentre', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), 'G597', '6001001659796', 'CHECKERS FX ROBBERG BAY', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), 'G598', '6001001659895', 'CHECKERS FX KLEIN KAROO', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), 'G599', '6001001659994', 'CHECKERS FOODS MILKWOOD SQUARE', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), 'G978', '6001001697897', 'CHECKERS FX MONTAGU VILLAGE', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), 'G980', '6001001698092', 'CHECKERS FX GARDEN WALK', 'Store', true, 'seed:shoprite-po-history-2026-10-08', now(), now())
ON CONFLICT ("BranchCode") DO NOTHING;
""");

            migrationBuilder.Sql("""
INSERT INTO shoprite_trade_items ("Id", "AcumaticaInventoryId", "AcumaticaUom", "DeliversTo", "Gtin", "AcumaticaUnitsPerShopriteUnit", "ShopritePackSize", "ShopriteUom", "IsVerified", "UpdatedBy", "CreatedAt", "UpdatedAt") VALUES
(gen_random_uuid(), 'ENER12', 'BOX', 'Store', '06001197181125', 1, 20, 'EA', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), 'ENER9', 'BOX', 'Store', '06001197181132', 1, 20, 'EA', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), 'ENER10', 'BOX', 'Store', '06001197181156', 1, 20, 'EA', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), 'ENER13', 'BOX', 'Store', '06001197011231', 1, 20, 'EA', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), 'ENER17', 'BOX', 'Store', '06001197181187', 1, 20, 'EA', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), 'ENER11', 'BOX', 'Store', '06001197181149', 1, 20, 'EA', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), 'ENER1', 'BOX', 'DistributionCentre', '06001197010432', 10, 10, 'EA', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), 'ENER10', 'BOX', 'DistributionCentre', '06001197040170', 12, 240, 'EA', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), 'ENER11', 'BOX', 'DistributionCentre', '06001197011149', 12, 240, 'EA', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), 'ENER12', 'BOX', 'DistributionCentre', '06001197020509', 12, 240, 'EA', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), 'ENER13', 'BOX', 'DistributionCentre', '06001197011255', 12, 240, 'EA', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), 'ENER17', 'BOX', 'DistributionCentre', '06001197040378', 12, 240, 'EA', false, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), 'ENER3', 'BOX', 'DistributionCentre', '06001197010456', 10, 10, 'EA', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), 'ENER47', 'BOX', 'DistributionCentre', '06001197040279', 12, 240, 'EA', false, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), 'ENER7', 'BOX', 'DistributionCentre', '06001197011323', 10, 10, 'EA', true, 'seed:shoprite-po-history-2026-10-08', now(), now()),
(gen_random_uuid(), 'ENER9', 'BOX', 'DistributionCentre', '06001197020585', 12, 240, 'EA', true, 'seed:shoprite-po-history-2026-10-08', now(), now())
ON CONFLICT ("AcumaticaInventoryId", "AcumaticaUom", "DeliversTo") DO NOTHING;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shoprite_delivery_locations");

            migrationBuilder.DropTable(
                name: "shoprite_trade_items");
        }
    }
}
