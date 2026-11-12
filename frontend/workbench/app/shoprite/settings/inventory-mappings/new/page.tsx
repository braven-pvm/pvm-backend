import { notFound } from "next/navigation";
import { saveInventoryMappingAction } from "../../../../actions";
import {
  getAcumaticaInventoryItem,
  getShopriteCatalogItems,
  type AcumaticaInventoryItem,
} from "../../../../../src/api/client";
import { hasAnyRole, requireWorkbenchUser } from "../../../../../src/auth/session";
import {
  Button,
  ButtonLink,
  FormActions,
  FormLayout,
  KeyValueList,
  Notice,
  Panel,
  SelectField,
  Stack,
  SubmitButton,
  TaskPage,
  TextField,
} from "../../../../../src/ui";
import { defaultShopriteUnit, shopriteUnits } from "../mapping-rules";

export const dynamic = "force-dynamic";
export const metadata = { title: "Add or change a mapping" };

type NewMappingPageProps = {
  searchParams: Promise<{ newSku?: string; uom?: string; shopriteItem?: string; mappingError?: string }>;
};

const steps = ["Find the Acumatica item", "Choose the Shoprite item and unit", "Done"];

export default async function NewMappingPage({ searchParams }: NewMappingPageProps) {
  const user = await requireWorkbenchUser("/shoprite/settings/inventory-mappings/new");
  if (!hasAnyRole(user, ["Admin"])) {
    notFound();
  }

  const params = await searchParams;
  const sku = params.newSku?.trim();
  const catalog = await getShopriteCatalogItems();
  const selectedItem = catalog.find((item) => item.shopriteBuyerItemId === params.shopriteItem);

  let item: AcumaticaInventoryItem | null = null;
  let lookupError: string | undefined;
  if (sku) {
    try {
      item = await getAcumaticaInventoryItem(sku);
    } catch (error) {
      lookupError = error instanceof Error ? error.message : "The Acumatica lookup failed.";
    }
  }

  const header = {
    eyebrow: "Shoprite · inventory mappings",
    title: "Add or change a mapping",
    description: "Find the item in Acumatica, then choose the Shoprite item and unit it maps to. Saving checks every affected invoice again.",
  };

  return (
    <TaskPage back={{ href: "/shoprite/settings/inventory-mappings", label: "Inventory mappings" }} header={header} steps={steps} current={item ? 1 : 0}>
      {params.mappingError ? (
        <Notice tone="error" title="Mapping not saved" live>
          {params.mappingError}
        </Notice>
      ) : null}

      {!item ? (
        <Panel>
          <form method="get" action="/shoprite/settings/inventory-mappings/new">
            {params.shopriteItem ? <input type="hidden" name="shopriteItem" value={params.shopriteItem} /> : null}
            <FormLayout>
              {sku && !lookupError ? (
                <Notice tone="warning" title={`${sku} is not an Acumatica item`}>
                  Check the inventory ID in Acumatica and try again. The ID must match exactly.
                </Notice>
              ) : null}
              {lookupError ? (
                <Notice tone="error" title="Acumatica did not answer">
                  {lookupError} Try again in a minute.
                </Notice>
              ) : null}
              <TextField
                name="newSku"
                label="Acumatica inventory ID"
                hint="Exactly as in Acumatica, for example ENER10"
                width="m"
                required
                defaultValue={sku}
              />
              <FormActions>
                <Button type="submit" variant="primary">
                  Find item
                </Button>
              </FormActions>
            </FormLayout>
          </form>
        </Panel>
      ) : (
        <Stack gap="m">
          <Panel title="Acumatica item">
            <KeyValueList
              items={[
                { label: "Inventory ID", value: item.inventoryId, mono: true },
                { label: "Description", value: item.description || null },
                { label: "Status", value: item.status },
                { label: "Units", value: item.unitsOfMeasure.join(", "), mono: true },
              ]}
            />
          </Panel>
          <Panel title="Shoprite item and unit">
            <form action={saveInventoryMappingAction}>
              <input type="hidden" name="inventoryId" value={item.inventoryId} />
              <FormLayout>
                <SelectField
                  name="acumaticaUom"
                  label="Acumatica unit"
                  width="m"
                  required
                  placeholder="Choose a unit"
                  defaultValue={params.uom && item.unitsOfMeasure.includes(params.uom) ? params.uom : undefined}
                  options={item.unitsOfMeasure.map((unit) => ({ value: unit, label: unit }))}
                />
                <SelectField
                  name="purchaseOrderLineId"
                  label="Shoprite item"
                  hint="Items seen on Shoprite purchase orders"
                  width="full"
                  required
                  placeholder="Choose a Shoprite item"
                  defaultValue={selectedItem?.representativePurchaseOrderLineId}
                  options={catalog.map((catalogItem) => ({
                    value: catalogItem.representativePurchaseOrderLineId,
                    label: `${catalogItem.shopriteBuyerItemId} · ${catalogItem.gtins.join(", ") || "no GTIN"} · ${catalogItem.description ?? "No description"}`,
                  }))}
                />
                <SelectField
                  name="shopriteUom"
                  label="Shoprite unit"
                  hint="EA each, CA carton, CS case, KG kilogram"
                  width="s"
                  required
                  placeholder="Choose"
                  defaultValue={defaultShopriteUnit(selectedItem) || undefined}
                  options={shopriteUnits.map((unit) => ({ value: unit, label: unit }))}
                />
                <TextField name="reason" label="Reason for the audit log" hint="Why this mapping is correct" width="l" required />
                <FormActions>
                  <SubmitButton pendingLabel="Saving…">Save mapping</SubmitButton>
                  <ButtonLink href="/shoprite/settings/inventory-mappings/new" variant="tertiary">
                    Choose another item
                  </ButtonLink>
                </FormActions>
              </FormLayout>
            </form>
          </Panel>
        </Stack>
      )}
    </TaskPage>
  );
}
