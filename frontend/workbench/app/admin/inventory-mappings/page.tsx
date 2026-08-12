import { saveInventoryMappingAction } from "../../actions";
import {
  getInventoryMappings,
  type InventoryMappingView,
} from "../../../src/api/client";
import { hasAnyRole, requireWorkbenchUser } from "../../../src/auth/session";

export const dynamic = "force-dynamic";

type InventoryMappingsPageProps = {
  searchParams: Promise<{ search?: string }>;
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

  const { search } = await searchParams;
  const mappings = await getInventoryMappings(search);
  const unresolved = mappings.filter(
    (mapping) => mapping.unresolvedCandidateCount > 0,
  ).length;
  const configured = mappings.filter(
    (mapping) => isConfigured(mapping),
  ).length;

  return (
    <main className="page-shell mapping-page">
      <section className="page-heading">
        <div>
          <h1>Inventory mappings</h1>
          <p>
            Product mappings are configured globally and reused automatically.
            Review only missing or conflicting Shoprite item, GTIN and UOM assignments.
          </p>
        </div>
      </section>

      <section className="metric-strip" aria-label="Mapping summary">
        <div>
          <span>Observed SKU/UOMs</span>
          <strong>{mappings.length}</strong>
        </div>
        <div>
          <span>Configured</span>
          <strong>{configured}</strong>
        </div>
        <div>
          <span>Needs configuration</span>
          <strong>{unresolved}</strong>
        </div>
      </section>

      <form className="mapping-filter" method="get">
        <label htmlFor="mapping-search">Search inventory configuration</label>
        <div>
          <input
            defaultValue={search ?? ""}
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
            <p>Clear the search or refresh finalized invoice discovery.</p>
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
        <span>
          Acumatica GTIN: {mapping.acumaticaGtins.join(", ") || "Not exposed"}
        </span>
        <span>
          Shoprite items: {formatItemMappings(mapping)}
        </span>
        <span>
          UOM: {mapping.acumaticaUom} → {mapping.uomMapping?.shopriteUom ?? "Not mapped"}
        </span>
        {mapping.uomMapping ? (
          <span>
            Last verified by {mapping.uomMapping.updatedBy} on{" "}
            {new Date(mapping.uomMapping.updatedAt).toLocaleDateString("en-ZA")}
          </span>
        ) : null}
        <span>
          {mapping.affectedCandidateCount} affected · {mapping.unresolvedCandidateCount} unresolved
        </span>
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
              <option
                key={suggestion.purchaseOrderLineId}
                value={suggestion.purchaseOrderLineId}
              >
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
        ) : (
          <span>-</span>
        )}
      </td>
      <td data-label="Change reason">
        {mapping.suggestions.length > 0 ? (
          <input
            form={formId}
            name="reason"
            placeholder="Required reason"
            required
            type="text"
          />
        ) : (
          <span>-</span>
        )}
      </td>
      <td className="table-action" data-label="Action">
        {mapping.suggestions.length > 0 ? (
          <form action={saveInventoryMappingAction} id={formId}>
            <input name="inventoryId" type="hidden" value={mapping.inventoryId} />
            <input name="acumaticaUom" type="hidden" value={mapping.acumaticaUom} />
            <button className="button secondary" type="submit">
              Save mapping
            </button>
          </form>
        ) : (
          <span>Refresh PO context</span>
        )}
      </td>
    </tr>
  );
}

function isConfigured(mapping: InventoryMappingView) {
  const hasVerifiedItem = mapping.itemMappings.some(
    (itemMapping) => itemMapping.isVerified,
  );
  const hasUsableGtin = mapping.acumaticaGtins.length > 0 || hasVerifiedItem;
  return hasUsableGtin && mapping.uomMapping?.isVerified === true;
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
    (suggestion) =>
      suggestion.gtin && mapping.acumaticaGtins.includes(suggestion.gtin),
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
