import { Plus } from "lucide-react";
import {
  getInventoryMappings,
  getShopriteCatalogItems,
  type InventoryMappingView,
  type ShopriteCatalogItem,
} from "../../../../src/api/client";
import { hasAnyRole, requireWorkbenchUser } from "../../../../src/auth/session";
import {
  ButtonLink,
  DataTable,
  EmptyState,
  FilterBar,
  ListPage,
  Notice,
  Panel,
  StatusBadge,
  TextField,
  type Column,
} from "../../../../src/ui";
import { ShopriteSections } from "../../shoprite-sections";
import { describeItemMappings, isConfigured, selectedSuggestion } from "./mapping-rules";

export const dynamic = "force-dynamic";
export const metadata = { title: "Inventory mappings" };

type InventoryMappingsPageProps = {
  searchParams: Promise<{ search?: string; mappingStatus?: string }>;
};

function newMappingHref(params: { sku?: string; uom?: string; shopriteItem?: string | null }) {
  const query = new URLSearchParams();
  if (params.sku) query.set("newSku", params.sku);
  if (params.uom) query.set("uom", params.uom);
  if (params.shopriteItem) query.set("shopriteItem", params.shopriteItem);
  const suffix = query.toString();
  return `/shoprite/settings/inventory-mappings/new${suffix ? `?${suffix}` : ""}`;
}

const unmappedColumns: Column<ShopriteCatalogItem>[] = [
  { key: "item", header: "Shoprite item", render: (item) => item.shopriteBuyerItemId, mono: true },
  { key: "description", header: "Description", render: (item) => item.description ?? "No description" },
  { key: "gtin", header: "GTIN", render: (item) => item.gtins.join(", ") || "—", mono: true },
  { key: "unit", header: "Unit", render: (item) => item.measurementUnitCodes.join(", ") || "Not supplied", mono: true },
  { key: "orders", header: "Orders", render: (item) => item.purchaseOrderCount, align: "end" },
  {
    key: "action",
    header: "Action",
    render: (item) => (
      <ButtonLink href={newMappingHref({ sku: item.supplierItemIds[0], shopriteItem: item.shopriteBuyerItemId })} variant="tertiary">
        Map item
      </ButtonLink>
    ),
  },
];

const mappingColumns: Column<InventoryMappingView>[] = [
  { key: "item", header: "Acumatica item", render: (mapping) => mapping.inventoryId, mono: true },
  { key: "description", header: "Description", render: (mapping) => mapping.description ?? "No description" },
  { key: "uom", header: "Unit", render: (mapping) => `${mapping.acumaticaUom} → ${mapping.uomMapping?.shopriteUom ?? "?"}`, mono: true },
  { key: "shoprite", header: "Shoprite item", render: (mapping) => describeItemMappings(mapping) },
  {
    key: "invoices",
    header: "Invoices",
    render: (mapping) => `${mapping.affectedCandidateCount} · ${mapping.unresolvedCandidateCount} open`,
    align: "end",
  },
  {
    key: "status",
    header: "Status",
    render: (mapping) =>
      isConfigured(mapping) ? <StatusBadge status="Configured" /> : <StatusBadge status="NeedsReview" label="Needs configuration" />,
  },
  {
    key: "action",
    header: "Action",
    render: (mapping) => (
      <ButtonLink
        href={newMappingHref({ sku: mapping.inventoryId, uom: mapping.acumaticaUom, shopriteItem: selectedSuggestion(mapping)?.shopriteBuyerItemId })}
        variant="tertiary"
      >
        {isConfigured(mapping) ? "Change" : "Configure"}
      </ButtonLink>
    ),
  },
];

export default async function InventoryMappingsPage({ searchParams }: InventoryMappingsPageProps) {
  const user = await requireWorkbenchUser("/shoprite/settings/inventory-mappings");
  const isAdmin = hasAnyRole(user, ["Admin"]);
  const params = await searchParams;

  if (!isAdmin) {
    return (
      <ListPage
        header={{ eyebrow: "Shoprite", title: "Inventory mappings" }}
        sections={<ShopriteSections current="inventory-mappings" isAdmin={false} />}
        notice={<Notice tone="info" title="Admin access needed">Inventory mappings change how every Shoprite invoice is built, so only an Admin can see and change them.</Notice>}
        table={null}
      />
    );
  }

  const [mappings, catalog] = await Promise.all([getInventoryMappings(params.search), getShopriteCatalogItems()]);
  const unmapped = catalog.filter((item) => !item.isMapped);

  return (
    <ListPage
      header={{
        eyebrow: "Shoprite",
        title: "Inventory mappings",
        description: "How each Acumatica item and unit maps to a Shoprite item and unit. Mappings apply to every Shoprite invoice.",
        actions: (
          <ButtonLink href="/shoprite/settings/inventory-mappings/new" variant="primary">
            <Plus aria-hidden="true" />
            Add mapping
          </ButtonLink>
        ),
      }}
      sections={<ShopriteSections current="inventory-mappings" isAdmin />}
      notice={
        params.mappingStatus ? (
          <Notice tone="success" title="Mapping saved" live>
            {params.mappingStatus}
          </Notice>
        ) : null
      }
      attention={
        unmapped.length > 0 ? (
          <Panel
            flush
            title={unmapped.length === 1 ? "1 Shoprite item has no mapping" : `${unmapped.length} Shoprite items have no mapping`}
            description="Seen on Shoprite purchase orders. Invoices with these items cannot be sent until they are mapped."
          >
            <DataTable
              caption="Shoprite items without a mapping"
              columns={unmappedColumns}
              rows={unmapped}
              rowKey={(item) => item.shopriteBuyerItemId}
              empty={null}
            />
          </Panel>
        ) : null
      }
      filters={
        <FilterBar action="/shoprite/settings/inventory-mappings" label="Search mappings" activeCount={params.search ? 1 : 0}>
          <TextField
            name="search"
            label="Item, description, Shoprite item or GTIN"
            type="search"
            width="l"
            showOptional={false}
            defaultValue={params.search}
          />
        </FilterBar>
      }
      table={
        <DataTable
          caption="Inventory mappings"
          columns={mappingColumns}
          rows={mappings}
          rowKey={(mapping) => `${mapping.inventoryId}:${mapping.acumaticaUom}`}
          empty={
            params.search ? (
              <EmptyState title="No mappings match this search" action={<ButtonLink href="/shoprite/settings/inventory-mappings">Show all mappings</ButtonLink>} />
            ) : (
              <EmptyState title="No mappings yet" action={<ButtonLink href="/shoprite/settings/inventory-mappings/new">Add mapping</ButtonLink>} />
            )
          }
        />
      }
    />
  );
}
