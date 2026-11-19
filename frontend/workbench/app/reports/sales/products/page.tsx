import { formatFigure } from "../../../../src/sales-report";
import { Delta, Notice, Panel, StatTile, TileGrid } from "../../../../src/ui";
import { byDimension, lastTwelveMonths, parentOf } from "../data";
import { drillHref, readContext, SalesFrame, sectionHref } from "../frame";
import { basePath, requireReportUser, single, type Search } from "../parts";
import { FigureTable, figureValues, MoreLink, pathKey, readOrder, short, sortRows, sparkColumn } from "../views";

export const dynamic = "force-dynamic";
export const metadata = { title: "Products · Sales report" };

const path = `${basePath}/products`;

/** Sales by product group and product, with units and the number of customers who buy each product. */
export default async function SalesProductsPage({ searchParams }: { searchParams: Promise<Search> }) {
  await requireReportUser(path);
  const search = await searchParams;
  const context = await readContext(search);
  const { state } = context;
  const shown = Math.max(25, Number.parseInt(single(search.shown) ?? "25", 10) || 25);
  const chosen = state.include.itemGroup ?? [];

  let data;
  try {
    const withoutGroups = { ...state, include: { ...state.include, itemGroup: [] } };
    const [groups, products, units, buyers, months] = await Promise.all([
      byDimension(withoutGroups, "itemGroup"),
      byDimension(state, "product"),
      byDimension(state, "product", "quantity"),
      byDimension(state, "product", "customers"),
      lastTwelveMonths(state, "product"),
    ]);
    data = { groups, products, units, buyers, months, groupOf: await parentOf(state, "itemGroup", "product", groups.rows) };
  } catch {
    data = undefined;
  }

  const unitsOf = new Map((data?.units.rows ?? []).map((row) => [row.key, row.current]));
  const buyersOf = new Map((data?.buyers.rows ?? []).map((row) => [row.key, row.current]));
  const order = readOrder(search, { key: "sales", direction: "desc" });
  const values = { ...figureValues, units: (row: { key: string }) => unitsOf.get(row.key) ?? 0, buyers: (row: { key: string }) => buyersOf.get(row.key) ?? 0 };
  const keep = (extra: Record<string, string>) => sectionHref(context, path, { ...(single(search.order) ? { order: single(search.order)! } : {}), ...extra });
  const toggleGroup = (key: string) => {
    const next = chosen.includes(key) ? chosen.filter((item) => item !== key) : [...chosen, key];
    return sectionHref(context, path, { itemGroup: next.join(",") });
  };
  const rows = data ? sortRows(data.products.rows, order, values) : [];

  return (
    <SalesFrame
      context={context}
      path={path}
      tab="Products"
      eyebrow="Sales report"
      title="Products"
      description="Sales by product group and product, with units and the number of customers who buy each product."
      download={["itemGroup", "product"]}
      message={data?.products.message}
    >
      {!data ? (
        <Notice tone="error" title="The sales report could not be read">
          Try again in a few minutes.
        </Notice>
      ) : (
        <>
          <TileGrid min={210}>
            {data.groups.rows
              .filter((row) => row.current > 0 || row.previous > 0)
              .map((row) => (
                <StatTile
                  key={row.key}
                  label={`${chosen.includes(row.key) ? "✓ " : ""}${row.label}`}
                  value={short(row.current)}
                  detail={
                    <>
                      <Delta current={row.current} previous={row.previous} /> against last year
                    </>
                  }
                  href={toggleGroup(row.key)}
                />
              ))}
          </TileGrid>
          <Panel flush title={chosen.length > 0 ? "Products of the selected groups" : "All products"} description="Click a group above to narrow the list. Click it again to show it no more.">
            <FigureTable
              caption="Sales by product"
              nameHeader="Product"
              rows={rows.slice(0, shown)}
              order={order}
              orderHref={(key, direction) => keep({ order: `${key}:${direction}` })}
              rowHref={(row) => drillHref(context, `${path}/${pathKey(row.key)}`)}
              meta={(row) => data.groupOf.get(row.key)}
              extra={[
                { key: "units", header: "Units", align: "end", sortable: true, render: (row) => formatFigure(unitsOf.get(row.key) ?? 0) },
                { key: "buyers", header: "Customers", align: "end", sortable: true, render: (row) => formatFigure(buyersOf.get(row.key) ?? 0) },
                sparkColumn((row) => data.months.get(row.key) ?? []),
              ]}
              extraValues={values}
            />
            <MoreLink total={rows.length} shown={shown} href={keep({ shown: String(shown + 25) })} />
          </Panel>
        </>
      )}
    </SalesFrame>
  );
}
