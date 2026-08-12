import Link from "next/link";
import { saveInventoryMappingAction } from "../../actions";
import {
  getAcumaticaInventoryItem,
  getInventoryMappings,
  getShopriteCatalogItems,
  type AcumaticaInventoryItem,
  type InventoryMappingView,
  type ShopriteCatalogItem,
} from "../../../src/api/client";
import { hasAnyRole, requireWorkbenchUser } from "../../../src/auth/session";

export const dynamic = "force-dynamic";

type InventoryMappingsPageProps = {
  searchParams: Promise<{
    search?: string;
    newSku?: string;
    shopriteItem?: string;
    mappingError?: string;
    mappingStatus?: string;
  }>;
};

export default async function InventoryMappingsPage({
  searchParams,
}: InventoryMappingsPageProps) {
  const user = await requireWorkbenchUser("/admin/inventory-mappings");
  if (!hasAnyRole(user, ["Admin"])) {
    return (
      <main className="page-shell">
        <section className="page-heading">
          <div>
            <h1>Inventory mappings</h1>
            <p>You need Admin access to manage integration configuration.</p>
          </div>
        </section>
      </main>
    );
  }

  const params = await searchParams;
  const [mappings, shopriteCatalog] = await Promise.all([
    getInventoryMappings(params.search),
    getShopriteCatalogItems(),
  ]);
  let inventoryItem: AcumaticaInventoryItem | null | undefined;
  let inventoryLookupError: string | undefined;
  if (params.newSku) {
    try {
      inventoryItem = await getAcumaticaInventoryItem(params.newSku);
    } catch (error) {
      inventoryLookupError = error instanceof Error
        ? error.message
        : "Acumatica inventory lookup failed.";
    }
  }
  const exceptions = shopriteCatalog.filter((item) => !item.isMapped);
  const configured = mappings.filter((mapping) => isConfigured(mapping)).length;
  const selectedShopriteItem = selectShopriteItem(shopriteCatalog, params.shopriteItem);

  return (
    <main className="page-shell mapping-page">
      <section className="page-heading">
        <div>
          <h1>Inventory mappings</h1>
          <p>
            Product mappings are global. New Shoprite PO items appear as exceptions
            until an Admin links them to a validated Acumatica SKU and UOM.
          </p>
        </div>
        <Link className="button primary" href="#add-mapping">
          Add mapping
        </Link>
      </section>

      {params.mappingError ? (
        <div className="alert-banner alert-error" role="alert">
          <strong>Mapping not saved</strong>
          <span>{params.mappingError}</span>
        </div>
      ) : null}
      {params.mappingStatus ? (
        <div className="alert-banner alert-success" role="status">
          <strong>Mapping saved</strong>
          <span>{params.mappingStatus}</span>
        </div>
      ) : null}

      <section className="metric-strip" aria-label="Mapping summary">
        <div>
          <span>Global SKU/UOMs</span>
          <strong>{configured}</strong>
        </div>
        <div>
          <span>Shoprite items</span>
          <strong>{shopriteCatalog.length}</strong>
        </div>
        <div>
          <span>Unmapped exceptions</span>
          <strong>{exceptions.length}</strong>
        </div>
      </section>

      <ShopriteExceptions exceptions={exceptions} />

      <AddMappingTool
        inventoryItem={inventoryItem}
        inventoryLookupError={inventoryLookupError}
        requestedSku={params.newSku}
        selectedShopriteItem={selectedShopriteItem}
        shopriteCatalog={shopriteCatalog}
      />

      <form className="mapping-filter" method="get">
        <label htmlFor="mapping-search">Search global configuration</label>
        <div>
          <input
            defaultValue={params.search ?? ""}
            id="mapping-search"
            name="search"
            placeholder="SKU, description, buyer item or GTIN"
            type="search"
          />
          <button className="button secondary" type="submit">
            Search mappings
          </button>
        </div>
      </form>

      <section className="table-panel mapping-list">
        <div className="table-toolbar">
          <h2>Reusable mapping configuration</h2>
          <span>{mappings.length} records</span>
        </div>
        {mappings.length === 0 ? (
          <div className="empty-state">
            <strong>No matching inventory configuration</strong>
            <p>Clear the search or add a mapping above.</p>
          </div>
        ) : (
          <table className="mapping-table inventory-mapping-table">
            <thead>
              <tr>
                <th>Acumatica inventory</th>
                <th>Current configuration</th>
                <th>Shoprite item</th>
                <th>Shoprite UOM</th>
                <th>Change reason</th>
                <th aria-label="Action" />
              </tr>
            </thead>
            <tbody>
              {mappings.map((mapping) => (
                <InventoryMappingRow
                  key={`${mapping.inventoryId}:${mapping.acumaticaUom}`}
                  mapping={mapping}
                />
              ))}
            </tbody>
          </table>
        )}
      </section>
    </main>
  );
}

function ShopriteExceptions({ exceptions }: { exceptions: ShopriteCatalogItem[] }) {
  return (
    <section className="table-panel exception-list">
      <div className="table-toolbar">
        <div>
          <h2>Unmapped Shoprite items</h2>
          <p>Seeded automatically from refreshed purchase orders.</p>
        </div>
        <span>{exceptions.length} exceptions</span>
      </div>
      {exceptions.length === 0 ? (
        <div className="empty-state compact-empty-state">
          <strong>No Shoprite item exceptions</strong>
          <p>Every known Shoprite buyer item has a global Acumatica mapping.</p>
        </div>
      ) : (
        <table className="mapping-table exception-table">
          <thead>
            <tr>
              <th>Shoprite item</th>
              <th>Observed identifiers</th>
              <th>PO coverage</th>
              <th>Suggested SKU</th>
              <th aria-label="Action" />
            </tr>
          </thead>
          <tbody>
            {exceptions.map((item) => {
              const suggestedSku = item.supplierItemIds[0] ?? "";
              const query = new URLSearchParams({
                shopriteItem: item.shopriteBuyerItemId,
                ...(suggestedSku ? { newSku: suggestedSku } : {}),
              });
              return (
                <tr key={item.shopriteBuyerItemId}>
                  <td data-label="Shoprite item">
                    <strong>{item.shopriteBuyerItemId}</strong>
                    <span>{item.description ?? "No description"}</span>
                  </td>
                  <td data-label="Observed identifiers">
                    <span>GTIN: {item.gtins.join(", ")}</span>
                    <span>UOM: {item.measurementUnitCodes.join(", ") || "Not supplied"}</span>
                  </td>
                  <td data-label="PO coverage">
                    <span>{item.purchaseOrderCount} purchase orders</span>
                    <span>Latest: {item.latestPurchaseOrderNumber}</span>
                  </td>
                  <td data-label="Suggested SKU">
                    {suggestedSku || "No supplier SKU supplied"}
                  </td>
                  <td className="table-action" data-label="Action">
                    <Link className="button secondary" href={`?${query.toString()}#add-mapping`}>
                      Map item
                    </Link>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
    </section>
  );
}

function AddMappingTool({
  inventoryItem,
  inventoryLookupError,
  requestedSku,
  selectedShopriteItem,
  shopriteCatalog,
}: {
  inventoryItem?: AcumaticaInventoryItem | null;
  inventoryLookupError?: string;
  requestedSku?: string;
  selectedShopriteItem?: ShopriteCatalogItem;
  shopriteCatalog: ShopriteCatalogItem[];
}) {
  return (
    <section className="mapping-create-tool" id="add-mapping">
      <div className="section-heading">
        <div>
          <h2>Add or preconfigure mapping</h2>
          <p>Validate the exact SKU in Acumatica, then assign a known Shoprite item and UOM.</p>
        </div>
      </div>

      <form className="sku-lookup-form" method="get">
        {selectedShopriteItem ? (
          <input name="shopriteItem" type="hidden" value={selectedShopriteItem.shopriteBuyerItemId} />
        ) : null}
        <label htmlFor="new-sku">Acumatica inventory SKU</label>
        <div>
          <input
            defaultValue={requestedSku ?? ""}
            id="new-sku"
            name="newSku"
            placeholder="Exact SKU, for example ENER10"
            required
          />
          <button className="button secondary" type="submit">
            Validate SKU
          </button>
        </div>
      </form>

      {requestedSku && inventoryItem === null ? (
        <div className="inline-validation-error" role="alert">
          <strong>SKU not found in Acumatica</strong>
          <span>Check the inventory ID and validate it again.</span>
        </div>
      ) : null}
      {inventoryLookupError ? (
        <div className="inline-validation-error" role="alert">
          <strong>Acumatica validation unavailable</strong>
          <span>{inventoryLookupError}</span>
        </div>
      ) : null}

      {inventoryItem ? (
        <form action={saveInventoryMappingAction} className="mapping-create-form">
          <div className="validated-sku">
            <span>Validated Acumatica item</span>
            <strong>{inventoryItem.inventoryId}</strong>
            <p>{inventoryItem.description || "No description"}</p>
            <small>Status: {inventoryItem.status ?? "Not supplied"}</small>
          </div>
          <input name="inventoryId" type="hidden" value={inventoryItem.inventoryId} />
          <label>
            <span>Acumatica UOM</span>
            <select name="acumaticaUom" required>
              <option value="">Select configured UOM</option>
              {inventoryItem.unitsOfMeasure.map((uom) => (
                <option key={uom} value={uom}>{uom}</option>
              ))}
            </select>
          </label>
          <label>
            <span>Shoprite item</span>
            <select
              defaultValue={selectedShopriteItem?.representativePurchaseOrderLineId ?? ""}
              name="purchaseOrderLineId"
              required
            >
              <option value="">Select known Shoprite item</option>
              {shopriteCatalog.map((item) => (
                <option
                  key={item.shopriteBuyerItemId}
                  value={item.representativePurchaseOrderLineId}
                >
                  {item.shopriteBuyerItemId} · {item.gtins.join(", ")} · {item.description ?? "No description"}
                </option>
              ))}
            </select>
          </label>
          <label>
            <span>Shoprite UOM</span>
            <select
              defaultValue={defaultShopriteUom(selectedShopriteItem)}
              name="shopriteUom"
              required
            >
              <option value="">Select UOM</option>
              <option value="EA">EA</option>
              <option value="CA">CA</option>
              <option value="CS">CS</option>
              <option value="KG">KG</option>
            </select>
          </label>
          <label>
            <span>Change reason</span>
            <input name="reason" placeholder="Why this mapping is correct" required />
          </label>
          <button className="button primary" type="submit">
            Save global mapping
          </button>
        </form>
      ) : null}
    </section>
  );
}

function InventoryMappingRow({ mapping }: { mapping: InventoryMappingView }) {
  const formId = `mapping-${mapping.inventoryId}-${mapping.acumaticaUom}`;
  const selectedSuggestion = findSelectedSuggestion(mapping);
  const status = isConfigured(mapping) ? "Configured" : "Needs configuration";

  return (
    <tr>
      <td data-label="Acumatica inventory">
        <strong>{mapping.inventoryId}</strong>
        <span>{mapping.description ?? "No description"}</span>
        <span>Acumatica UOM: {mapping.acumaticaUom}</span>
      </td>
      <td data-label="Current configuration">
        <span className={`status-pill ${status === "Configured" ? "status-healthy" : "status-pending"}`}>
          {status}
        </span>
        <span>Acumatica GTIN: {mapping.acumaticaGtins.join(", ") || "Not exposed"}</span>
        <span>Shoprite items: {formatItemMappings(mapping)}</span>
        <span>UOM: {mapping.acumaticaUom} → {mapping.uomMapping?.shopriteUom ?? "Not mapped"}</span>
        {mapping.uomMapping ? (
          <span>
            Last verified by {mapping.uomMapping.updatedBy} on{" "}
            {new Date(mapping.uomMapping.updatedAt).toLocaleDateString("en-ZA")}
          </span>
        ) : null}
        <span>{mapping.affectedCandidateCount} affected · {mapping.unresolvedCandidateCount} unresolved</span>
      </td>
      <td data-label="Shoprite item">
        {mapping.suggestions.length > 0 ? (
          <select
            defaultValue={selectedSuggestion?.purchaseOrderLineId ?? ""}
            form={formId}
            name="purchaseOrderLineId"
            required
          >
            <option value="">Select observed PO item</option>
            {mapping.suggestions.map((suggestion) => (
              <option key={suggestion.purchaseOrderLineId} value={suggestion.purchaseOrderLineId}>
                {suggestion.shopriteBuyerItemId ?? "No buyer item"} · {suggestion.gtin ?? "No GTIN"} · {suggestion.description ?? "No description"} · PO {suggestion.purchaseOrderNumber}
              </option>
            ))}
          </select>
        ) : (
          <span>No matched PO item context</span>
        )}
      </td>
      <td data-label="Shoprite UOM">
        {mapping.suggestions.length > 0 ? (
          <select
            defaultValue={mapping.uomMapping?.shopriteUom ?? ""}
            form={formId}
            name="shopriteUom"
            required
          >
            <option value="">Select UOM</option>
            <option value="EA">EA</option>
            <option value="CA">CA</option>
            <option value="CS">CS</option>
            <option value="KG">KG</option>
          </select>
        ) : <span>-</span>}
      </td>
      <td data-label="Change reason">
        {mapping.suggestions.length > 0 ? (
          <input form={formId} name="reason" placeholder="Required reason" required type="text" />
        ) : <span>-</span>}
      </td>
      <td className="table-action" data-label="Action">
        {mapping.suggestions.length > 0 ? (
          <form action={saveInventoryMappingAction} id={formId}>
            <input name="inventoryId" type="hidden" value={mapping.inventoryId} />
            <input name="acumaticaUom" type="hidden" value={mapping.acumaticaUom} />
            <button className="button secondary" type="submit">Save mapping</button>
          </form>
        ) : <span>Refresh PO context</span>}
      </td>
    </tr>
  );
}

function isConfigured(mapping: InventoryMappingView) {
  const hasVerifiedItem = mapping.itemMappings.some((itemMapping) => itemMapping.isVerified);
  const hasUsableGtin = mapping.acumaticaGtins.length > 0 || hasVerifiedItem;
  return hasUsableGtin && mapping.uomMapping?.isVerified === true;
}

function selectShopriteItem(catalog: ShopriteCatalogItem[], buyerItemId?: string) {
  if (!buyerItemId) {
    return undefined;
  }
  return catalog.find((item) => item.shopriteBuyerItemId === buyerItemId);
}

function defaultShopriteUom(item?: ShopriteCatalogItem) {
  const observed = item?.measurementUnitCodes.find((uom) => ["EA", "CA", "CS", "KG"].includes(uom));
  return observed ?? "";
}

function findSelectedSuggestion(mapping: InventoryMappingView) {
  const configured = mapping.suggestions.find((suggestion) =>
    mapping.itemMappings.some(
      (itemMapping) =>
        itemMapping.shopriteBuyerItemId === suggestion.shopriteBuyerItemId &&
        itemMapping.gtin === suggestion.gtin,
    ),
  );
  if (configured) {
    return configured;
  }

  return mapping.suggestions.find(
    (suggestion) => suggestion.gtin && mapping.acumaticaGtins.includes(suggestion.gtin),
  );
}

function formatItemMappings(mapping: InventoryMappingView) {
  if (mapping.itemMappings.length === 0) {
    return "Not mapped";
  }

  return mapping.itemMappings
    .map(
      (itemMapping) =>
        `${itemMapping.shopriteBuyerItemId} / ${itemMapping.gtin}${itemMapping.isVerified ? " verified" : " unverified"}`,
    )
    .join(", ");
}
