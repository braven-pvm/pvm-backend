using Microsoft.EntityFrameworkCore;

namespace Pvm.Infrastructure.Persistence;

public static class ShopriteMappingSchemaInitializer
{
    public static async Task EnsureShopriteMappingSchemaAsync(
        this PvmDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            create table if not exists shoprite_item_mappings (
                "Id" uuid primary key,
                "AcumaticaInventoryId" character varying(128) not null,
                "ShopriteBuyerItemId" character varying(128) not null,
                "Gtin" character varying(32) not null,
                "IsVerified" boolean not null,
                "UpdatedBy" character varying(320) not null,
                "CreatedAt" timestamp with time zone not null,
                "UpdatedAt" timestamp with time zone not null
            );

            create unique index if not exists "IX_shoprite_item_mappings_AcumaticaInventoryId_ShopriteBuyerItemId"
                on shoprite_item_mappings ("AcumaticaInventoryId", "ShopriteBuyerItemId");

            create table if not exists shoprite_uom_mappings (
                "Id" uuid primary key,
                "AcumaticaInventoryId" character varying(128) not null,
                "AcumaticaUom" character varying(32) not null,
                "ShopriteUom" character varying(16) not null,
                "IsVerified" boolean not null,
                "UpdatedBy" character varying(320) not null,
                "CreatedAt" timestamp with time zone not null,
                "UpdatedAt" timestamp with time zone not null
            );

            create unique index if not exists "IX_shoprite_uom_mappings_AcumaticaInventoryId_AcumaticaUom"
                on shoprite_uom_mappings ("AcumaticaInventoryId", "AcumaticaUom");
            """,
            cancellationToken);
    }
}
