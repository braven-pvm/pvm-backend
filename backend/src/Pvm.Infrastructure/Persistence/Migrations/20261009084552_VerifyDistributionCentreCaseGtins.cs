using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pvm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VerifyDistributionCentreCaseGtins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AddShopriteReferenceData loaded these two distribution-centre case GTINs unverified,
            // because each was seen on one Shoprite order only. PVM confirmed both on 2026-10-09.
            migrationBuilder.Sql("""
UPDATE shoprite_trade_items
SET "IsVerified" = true,
    "UpdatedBy" = 'confirmed-by-pvm-2026-10-09',
    "UpdatedAt" = now()
WHERE "DeliversTo" = 'DistributionCentre'
  AND "IsVerified" = false
  AND ("AcumaticaInventoryId", "AcumaticaUom", "Gtin") IN
      (('ENER17', 'BOX', '06001197040378'),
       ('ENER47', 'BOX', '06001197040279'));
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
UPDATE shoprite_trade_items
SET "IsVerified" = false
WHERE "UpdatedBy" = 'confirmed-by-pvm-2026-10-09';
""");
        }
    }
}
