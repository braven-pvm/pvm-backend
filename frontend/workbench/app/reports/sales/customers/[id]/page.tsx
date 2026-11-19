import { Columns, EmptyState, FilterChips, Notice, Panel, StatTile, TabNav, TileGrid } from "../../../../../src/ui";
import { byDimension, customerBundle, financialYear, twoLevels } from "../../data";
import { drillHref, readContext, SalesFrame, sectionHref } from "../../frame";
import { basePath, requireReportUser, single, type Search } from "../../parts";
import { against, breakdownTabs, FigureTable, keyOf, lastOrderText, pathKey, readOrder, short, YearChart } from "../../views";
import { formatFigure } from "../../../../../src/sales-report";

export const dynamic = "force-dynamic";
export const metadata = { title: "Customer · Sales report" };

const breakdowns = [
  { id: "products", label: "Products" },
  { id: "groups", label: "Product groups" },
];

/** One customer: sales by month, status, the products it buys and the groups it does not buy. */
export default async function SalesCustomerPage({ params, searchParams }: { params: Promise<{ id: string }>; searchParams: Promise<Search> }) {
  const { id } = await params;
  const key = keyOf(id);
  const path = `${basePath}/customers/${pathKey(key)}`;
  await requireReportUser(path);
  const search = await searchParams;
  const context = await readContext(search, { dimension: "customer", key });
  const { state } = context;
  const by = breakdowns.find((item) => item.id === single(search.by))?.id ?? "products";

  let data;
  try {
    const withoutCustomer = { ...state, include: { ...state.include, customer: [] } };
    const [bundle, tree, units, allGroups, year] = await Promise.all([
      customerBundle(state),
      twoLevels(state, "itemGroup", "product"),
      byDimension(state, "product", "quantity"),
      byDimension(withoutCustomer, "itemGroup"),
      financialYear(state),
    ]);
    data = {
      customer: bundle.rows.find((row) => row.key === key),
      products: { rows: tree.children, current: bundle.current, previous: bundle.previous },
      units,
      groups: { rows: tree.parents },
      allGroups,
      meta: bundle.meta,
      year,
      message: tree.message,
    };
  } catch {
    data = undefined;
  }

  const name = data?.customer?.label ?? key;
  const unitsOf = new Map((data?.units.rows ?? []).map((row) => [row.key, row.current]));
  const bought = new Set((data?.groups.rows ?? []).filter((row) => row.current > 0).map((row) => row.key));
  const missing = (data?.allGroups.rows ?? []).filter((row) => row.key && !bought.has(row.key));
  const lastTwelve = data?.customer?.lastTwelve ?? [];
  const average = lastTwelve.slice(0, -1).reduce((sum, value) => sum + value, 0) / Math.max(1, lastTwelve.length - 1);
  const order = readOrder(search, { key: "sales", direction: "desc" });
  const here = (extra: Record<string, string>) => drillHref(context, path, undefined, { by, ...extra });
  const step = { dimension: "customer" as const, key };
  const showTable = single(search.months) === "1";
  const meta = data?.meta.get(key);

  return (
    <SalesFrame
      context={context}
      path={path}
      back={{ href: sectionHref(context, `${basePath}/customers`), label: "All customers" }}
      eyebrow={meta || "Customer"}
      title={name}
      description={`Customer ${key}.${meta ? ` ${meta}.` : ""}`}
      fixed={["customer"]}
      download={["product"]}
      message={data?.message}
    >
      {!data ? (
        <Notice tone="error" title="The sales report could not be read">
          Try again in a few minutes.
        </Notice>
      ) : !data.customer ? (
        <EmptyState title="No sales for this customer in these filters" description="Choose another period, or clear a filter." />
      ) : (
        <>
          <TileGrid min={210}>
            <StatTile label="Sales" value={short(data.customer.current)} detail={against(data.customer.current, data.customer.previous)} />
            <StatTile label="Status" value={data.customer.status} detail="For the period above" />
            <StatTile label="Last order" value={lastOrderText(data.customer.monthsSinceOrder).replace("Last order ", "")} detail="In the last 12 months" />
            <StatTile label="Average month" value={short(average)} detail="Over the last 11 full months" />
          </TileGrid>
          <Columns>
            <Panel title="Sales by month" description="This financial year against last year">
              <YearChart title={`Sales of ${name} by month`} months={data.year.months} showTable={showTable} tableHref={here(showTable ? {} : { months: "1" })} />
            </Panel>
            <Panel title="Groups it does not buy" description="Ideas for the next visit">
              {missing.length > 0 ? (
                <FilterChips
                  label="Product groups this customer does not buy"
                  chips={missing.map((row) => ({ label: row.label, href: sectionHref(context, `${basePath}/products`, { itemGroup: row.key }), current: false }))}
                />
              ) : (
                <EmptyState title="This customer buys from every group" />
              )}
            </Panel>
          </Columns>
          <Panel flush title={`${name} by ${breakdowns.find((item) => item.id === by)?.label.toLowerCase()}`} description="Click a row to go one level down. The customer stays selected.">
            <TabNav label="Break down by" tabs={breakdownTabs(by, breakdowns, (item) => drillHref(context, path, undefined, { by: item }))} />
            {by === "products" ? (
              <FigureTable
                caption={`Products of ${name}`}
                nameHeader="Product"
                rows={data.products.rows}
                order={order}
                orderHref={(column, direction) => here({ order: `${column}:${direction}` })}
                rowHref={(row) => drillHref(context, `${basePath}/products/${pathKey(row.key)}`, step)}
                extra={[{ key: "units", header: "Units", align: "end", sortable: true, render: (row) => formatFigure(unitsOf.get(row.key) ?? 0) }]}
                extraValues={{ units: (row) => unitsOf.get(row.key) ?? 0 }}
              />
            ) : (
              <FigureTable
                caption={`Product groups of ${name}`}
                nameHeader="Product group"
                rows={data.groups.rows}
                order={order}
                orderHref={(column, direction) => here({ order: `${column}:${direction}` })}
                rowHref={(row) => drillHref(context, path, undefined, { by: "products", itemGroup: row.key })}
                extra={[{ key: "share", header: "Share", align: "end", render: (row) => `${data.customer ? Math.round((row.current / Math.max(1, data.customer.current)) * 100) : 0}%` }]}
              />
            )}
          </Panel>
        </>
      )}
    </SalesFrame>
  );
}
