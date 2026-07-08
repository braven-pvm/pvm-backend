using Microsoft.EntityFrameworkCore;

namespace Pvm.Infrastructure.Persistence;

public static class ShopritePurchaseOrderSchemaInitializer
{
    public static async Task EnsureShopritePurchaseOrderSchemaAsync(
        this PvmDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            create table if not exists shoprite_purchase_orders (
                "Id" uuid primary key,
                "PurchaseOrderNumber" character varying(128) not null,
                "OrderHeaderId" character varying(128) null,
                "OrderTypeCode" character varying(32) null,
                "OrderTypeLabel" character varying(128) null,
                "SupplierGln" character varying(32) null,
                "BuyerGln" character varying(32) null,
                "DeliveryGln" character varying(32) null,
                "DeliveryLocationCode" character varying(128) null,
                "DeliveryLocationName" character varying(512) null,
                "DeliveryLocationSource" character varying(64) not null,
                "CurrencyCode" character varying(8) null,
                "TotalExcludingTax" numeric null,
                "TotalIncludingTax" numeric null,
                "TotalTax" numeric null,
                "SourceEnvironment" character varying(32) not null,
                "SourceEndpoint" character varying(128) not null,
                "PayloadHash" character varying(128) null,
                "RawOrderJson" jsonb null,
                "ShopriteCreatedAt" timestamp with time zone null,
                "ShopriteLastUpdatedAt" timestamp with time zone null,
                "FirstSeenAt" timestamp with time zone not null,
                "LastSeenAt" timestamp with time zone not null
            );

            create unique index if not exists "IX_shoprite_purchase_orders_PurchaseOrderNumber"
                on shoprite_purchase_orders ("PurchaseOrderNumber");

            create index if not exists "IX_shoprite_purchase_orders_DeliveryGln"
                on shoprite_purchase_orders ("DeliveryGln");

            create table if not exists shoprite_purchase_order_lines (
                "Id" uuid primary key,
                "ShopritePurchaseOrderId" uuid not null references shoprite_purchase_orders ("Id") on delete cascade,
                "LineNumber" integer not null,
                "Gtin" character varying(32) null,
                "BuyerItemId" character varying(128) null,
                "BuyerItemDescription" character varying(512) null,
                "SupplierItemId" character varying(128) null,
                "Description" character varying(512) null,
                "RequestedQuantity" numeric null,
                "MeasurementUnitCode" character varying(32) null,
                "NetAmount" numeric null,
                "NetPrice" numeric null,
                "MonetaryAmountExcludingTaxes" numeric null,
                "MonetaryAmountIncludingTaxes" numeric null
            );

            create unique index if not exists "IX_shoprite_purchase_order_lines_ShopritePurchaseOrderId_LineNumber"
                on shoprite_purchase_order_lines ("ShopritePurchaseOrderId", "LineNumber");

            create index if not exists "IX_shoprite_purchase_order_lines_Gtin"
                on shoprite_purchase_order_lines ("Gtin");

            alter table if exists invoice_candidates
                add column if not exists "MatchedShopritePurchaseOrderId" uuid null;

            create index if not exists "IX_invoice_candidates_MatchedShopritePurchaseOrderId"
                on invoice_candidates ("MatchedShopritePurchaseOrderId");

            do $$
            begin
                if not exists (
                    select 1
                    from pg_constraint constraint_record
                    join pg_class source_table
                        on source_table.oid = constraint_record.conrelid
                    join pg_namespace source_namespace
                        on source_namespace.oid = source_table.relnamespace
                    join pg_attribute source_column
                        on source_column.attrelid = source_table.oid
                        and source_column.attnum = any(constraint_record.conkey)
                    join pg_class target_table
                        on target_table.oid = constraint_record.confrelid
                    where constraint_record.contype = 'f'
                        and source_namespace.nspname = 'public'
                        and source_table.relname = 'invoice_candidates'
                        and target_table.relname = 'shoprite_purchase_orders'
                        and source_column.attname = 'MatchedShopritePurchaseOrderId'
                ) then
                    alter table invoice_candidates
                        add constraint "FK_invoice_candidates_shoprite_purchase_orders_MatchedShopritePurchaseOrderId"
                        foreign key ("MatchedShopritePurchaseOrderId")
                        references shoprite_purchase_orders ("Id")
                        on delete set null;
                end if;
            end $$;
            """,
            cancellationToken);
    }
}
