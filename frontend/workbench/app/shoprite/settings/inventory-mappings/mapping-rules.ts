import type { InventoryMappingView, ShopriteCatalogItem } from "../../../../src/api/client";

export const shopriteUnits = ["EA", "CA", "CS", "KG"] as const;

/** A mapping is usable when it has a GTIN (from Acumatica or a verified item) and a verified unit. */
export function isConfigured(mapping: InventoryMappingView) {
  const hasVerifiedItem = mapping.itemMappings.some((itemMapping) => itemMapping.isVerified);
  const hasUsableGtin = mapping.acumaticaGtins.length > 0 || hasVerifiedItem;
  return hasUsableGtin && mapping.uomMapping?.isVerified === true;
}

/** The Shoprite item that the row's current or suggested mapping points at. */
export function selectedSuggestion(mapping: InventoryMappingView) {
  const configured = mapping.suggestions.find((suggestion) =>
    mapping.itemMappings.some(
      (itemMapping) =>
        itemMapping.shopriteBuyerItemId === suggestion.shopriteBuyerItemId && itemMapping.gtin === suggestion.gtin,
    ),
  );
  return configured ?? mapping.suggestions.find((suggestion) => suggestion.gtin && mapping.acumaticaGtins.includes(suggestion.gtin));
}

export function describeItemMappings(mapping: InventoryMappingView) {
  if (mapping.itemMappings.length === 0) {
    return "Not mapped";
  }
  return mapping.itemMappings
    .map((itemMapping) => `${itemMapping.shopriteBuyerItemId} · ${itemMapping.gtin}${itemMapping.isVerified ? "" : " (not verified)"}`)
    .join(", ");
}

export function defaultShopriteUnit(item?: ShopriteCatalogItem) {
  return item?.measurementUnitCodes.find((unit) => (shopriteUnits as readonly string[]).includes(unit)) ?? "";
}
