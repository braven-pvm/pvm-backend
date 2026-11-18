import { redirect } from "next/navigation";
import { mapSellOutProductAction } from "../../actions";
import {
  getSellOutProductChoices,
  getUnmappedSellOutProducts,
  sellOutSources,
  type SellOutProductChoice,
  type UnmappedSellOutProduct,
} from "../../../src/api/sell-out";
import { hasAnyRole, requireWorkbenchUser } from "../../../src/auth/session";
import { describeMonths } from "../../../src/sell-out.mjs";
import { BackLink, EmptyState, FormActions, KeyValueList, Notice, PageFrame, PageHeader, Panel, SelectField, Stack, SubmitButton, TextField } from "../../../src/ui";

export const dynamic = "force-dynamic";
export const metadata = { title: "Map sell-out products" };

const header = {
  eyebrow: "Sell-out",
  title: "Map sell-out products",
  description: "Each retailer has its own product codes. Choose the Acumatica item for each code once. Later files use the same map.",
};

/** One form for each product code that maps to no Acumatica item. */
export default async function SellOutMappingPage() {
  const user = await requireWorkbenchUser("/sell-out/mapping");
  if (!hasAnyRole(user, ["Admin", "Operator"])) {
    redirect("/sell-out");
  }

  let unmapped: UnmappedSellOutProduct[] = [];
  let choices: SellOutProductChoice[] = [];
  let failed = false;
  try {
    [unmapped, choices] = await Promise.all([getUnmappedSellOutProducts(), getSellOutProductChoices()]);
  } catch {
    failed = true;
  }

  const options = choices.map((choice) => ({ value: choice.inventoryId, label: `${choice.inventoryId} · ${choice.description}` }));

  return (
    <PageFrame>
      <BackLink href="/sell-out" label="Sell-out" />
      <PageHeader {...header} />
      <Stack gap="m">
        {failed ? (
          <Notice tone="error" title="The products could not be read">
            Try again in a few minutes.
          </Notice>
        ) : null}
        {!failed && unmapped.length === 0 ? (
          <EmptyState title="Every product is mapped" description="All products in the loaded files map to Acumatica items." />
        ) : null}
        {!failed && unmapped.length > 0 && options.length === 0 ? (
          <Notice tone="warning" title="No Acumatica items to choose from yet">
            The item list comes from the sell-in load. Run it once, then come back.
          </Notice>
        ) : null}
        {unmapped.map((product) => (
          <Panel
            key={`${product.source}-${product.productCode}`}
            title={`${sellOutSources[product.source] ?? product.source} · ${product.productCode}`}
            description={`${product.units.toLocaleString("en-ZA")} items sold in ${product.stores} ${product.stores === 1 ? "store" : "stores"}, last in ${describeMonths([product.lastMonth])}.`}
          >
            <form action={mapSellOutProductAction}>
              <input type="hidden" name="source" value={product.source} />
              <input type="hidden" name="productCode" value={product.productCode} />
              <Stack gap="s">
                {product.gtin ? <KeyValueList items={[{ label: "Barcode in the file", value: product.gtin, mono: true }]} /> : null}
                <SelectField
                  name="inventoryId"
                  id={`item-${product.source}-${product.productCode}`}
                  label="Acumatica item"
                  options={options}
                  placeholder="Choose the item"
                  required
                  width="l"
                />
                <TextField
                  name="unitsPerEach"
                  id={`units-${product.source}-${product.productCode}`}
                  label="Single items in one unit of the file"
                  hint="Usually 1. Use 20 if the file counts boxes of 20."
                  inputMode="decimal"
                  defaultValue="1"
                  width="s"
                />
                <FormActions>
                  <SubmitButton pendingLabel="Saving…">Save the map</SubmitButton>
                </FormActions>
              </Stack>
            </form>
          </Panel>
        ))}
      </Stack>
    </PageFrame>
  );
}
