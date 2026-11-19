import { Columns, Notice, Panel, StatTile, TabNav, TileGrid } from "../../../../../src/ui";
import { byDimension, customerBundle, financialYear } from "../../data";
import { drillHref, readContext, SalesFrame, sectionHref } from "../../frame";
import { basePath, requireReportUser, single, type Search } from "../../parts";
import { against, breakdownTabs, customerColumns, customerValues, FigureBars, FigureTable, keyOf, pathKey, readOrder, short, YearChart } from "../../views";

export const dynamic = "force-dynamic";
export const metadata = { title: "Rep · Sales report" };

const breakdowns = [
  { id: "customers", label: "Customers" },
  { id: "products", label: "Products" },
  { id: "groups", label: "Product groups" },
];

/** One rep: the sales of the rep's customers, broken down by customer, product or product group. */
export default async function SalesRepPage({ params, searchParams }: { params: Promise<{ id: string }>; searchParams: Promise<Search> }) {
  const { id } = await params;
  const key = keyOf(id);
  const path = `${basePath}/reps/${pathKey(key)}`;
  await requireReportUser(path);
  const search = await searchParams;
  const context = await readContext(search, { dimension: "rep", key });
  const { state } = context;
  const by = breakdowns.find((item) => item.id === single(search.by))?.id ?? "customers";

  let data;
  try {
    const [bundle, groups, products, year] = await Promise.all([
      customerBundle(state),
      byDimension(state, "itemGroup"),
      by === "products" ? byDimension(state, "product") : Promise.resolve(undefined),
      financialYear(state),
    ]);
    data = { rep: { rows: bundle.reps, current: bundle.current, previous: bundle.previous, message: bundle.message }, customerRows: bundle, groups, products, year };
  } catch {
    data = undefined;
  }

  const name = data?.rep.rows.find((row) => row.key === key)?.label ?? (key || "No rep");
  const order = readOrder(search, { key: "sales", direction: "desc" });
  const here = (extra: Record<string, string>) => drillHref(context, path, undefined, { by, ...extra });
  const step = { dimension: "rep" as const, key };
  const rows = data?.customerRows.rows ?? [];
  const showTable = single(search.months) === "1";

  return (
    <SalesFrame
      context={context}
      path={path}
      back={{ href: sectionHref(context, `${basePath}/reps`), label: "All reps" }}
      eyebrow="Rep"
      title={name}
      description={`The sales of the customers of ${name}. The rep is the rep of the customer today, as in the workbook.`}
      fixed={["rep"]}
      download={["customer", "product"]}
      message={data?.rep.message}
    >
      {!data ? (
        <Notice tone="error" title="The sales report could not be read">
          Try again in a few minutes.
        </Notice>
      ) : (
        <>
          <TileGrid min={210}>
            <StatTile label="Sales" value={short(data.rep.current)} detail={against(data.rep.current, data.rep.previous)} />
            <StatTile label="Active customers" value={String(rows.filter((row) => row.current > 0).length)} detail={`${rows.filter((row) => row.status === "New").length} new this period`} />
            <StatTile label="Growing" value={String(rows.filter((row) => row.status === "Growing").length)} detail="Customers up more than 10%" />
            <StatTile label="To call" value={String(rows.filter((row) => row.status === "At risk" || row.status === "Lost").length)} detail="No order in 2 months or more" />
          </TileGrid>
          <Columns>
            <Panel title="Sales by month" description="This financial year against last year">
              <YearChart title={`Sales of ${name} by month`} months={data.year.months} showTable={showTable} tableHref={here(showTable ? {} : { months: "1" })} />
            </Panel>
            <Panel title="Product groups" description="Sales and change against last year">
              <FigureBars label={`Sales of ${name} by product group`} rows={data.groups.rows} href={(row) => drillHref(context, path, undefined, { by: "products", itemGroup: row.key })} />
            </Panel>
          </Columns>
          <Panel flush title={`${name} by ${breakdowns.find((item) => item.id === by)?.label.toLowerCase()}`} description="Click a row to go one level down. The rep stays selected.">
            <TabNav label="Break down by" tabs={breakdownTabs(by, breakdowns, (item) => drillHref(context, path, undefined, { by: item }))} />
            {by === "customers" ? (
              <FigureTable
                caption={`Customers of ${name}`}
                nameHeader="Customer"
                rows={rows}
                order={order}
                orderHref={(column, direction) => here({ order: `${column}:${direction}` })}
                rowHref={(row) => drillHref(context, `${basePath}/customers/${pathKey(row.key)}`, step)}
                extra={customerColumns()}
                extraValues={customerValues}
              />
            ) : by === "products" ? (
              <FigureTable
                caption={`Products of ${name}`}
                nameHeader="Product"
                rows={data.products?.rows ?? []}
                order={order}
                orderHref={(column, direction) => here({ order: `${column}:${direction}` })}
                rowHref={(row) => drillHref(context, `${basePath}/products/${pathKey(row.key)}`, step)}
              />
            ) : (
              <FigureTable
                caption={`Product groups of ${name}`}
                nameHeader="Product group"
                rows={data.groups.rows}
                order={order}
                orderHref={(column, direction) => here({ order: `${column}:${direction}` })}
                rowHref={(row) => drillHref(context, path, undefined, { by: "products", itemGroup: row.key })}
              />
            )}
          </Panel>
        </>
      )}
    </SalesFrame>
  );
}
