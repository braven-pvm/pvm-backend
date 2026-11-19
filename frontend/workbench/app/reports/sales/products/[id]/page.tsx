import { formatFigure } from "../../../../../src/sales-report";
import { Columns, Notice, Panel, StatTile, TabNav, TileGrid } from "../../../../../src/ui";
import { byDimension, customerBundle, financialYear, total, twoLevels } from "../../data";
import { drillHref, readContext, SalesFrame, sectionHref } from "../../frame";
import { basePath, requireReportUser, single, type Search } from "../../parts";
import {
  against,
  breakdownTabs,
  customerColumns,
  customerValues,
  FigureBars,
  FigureTable,
  figureValues,
  keyOf,
  MoreLink,
  pathKey,
  readOrder,
  short,
  sortRows,
  YearChart,
} from "../../views";

export const dynamic = "force-dynamic";
export const metadata = { title: "Product · Sales report" };

const breakdowns = [
  { id: "customers", label: "Customers" },
  { id: "reps", label: "Reps" },
  { id: "groups", label: "Customer groups" },
];

/** One product: sales by month, by rep, and the customers who buy it. */
export default async function SalesProductPage({ params, searchParams }: { params: Promise<{ id: string }>; searchParams: Promise<Search> }) {
  const { id } = await params;
  const key = keyOf(id);
  const path = `${basePath}/products/${pathKey(key)}`;
  await requireReportUser(path);
  const search = await searchParams;
  const context = await readContext(search, { dimension: "product", key });
  const { state } = context;
  const by = breakdowns.find((item) => item.id === single(search.by))?.id ?? "customers";
  const shown = Math.max(25, Number.parseInt(single(search.shown) ?? "25", 10) || 25);

  let data;
  try {
    const [bundle, tree, units, customerGroups, year] = await Promise.all([
      customerBundle(state),
      twoLevels(state, "itemGroup", "product"),
      total(state, "quantity"),
      byDimension(state, "customerGroup"),
      financialYear(state),
    ]);
    const repLabels = new Map(bundle.reps.map((row) => [row.key, row.label]));
    data = {
      product: { rows: tree.children, current: bundle.current, previous: bundle.previous, message: bundle.message },
      units,
      reps: { rows: bundle.reps },
      customerRows: bundle,
      customerGroups,
      group: tree.parents[0]?.label,
      year,
      meta: bundle.meta,
      repOf: new Map([...bundle.repOf].map(([customer, rep]) => [customer, repLabels.get(rep) ?? "No rep"])),
    };
  } catch {
    data = undefined;
  }

  const name = data?.product.rows.find((row) => row.key === key)?.label ?? key;
  const order = readOrder(search, { key: "sales", direction: "desc" });
  const here = (extra: Record<string, string>) => drillHref(context, path, undefined, { by, ...extra });
  const step = { dimension: "product" as const, key };
  const buyers = data?.customerRows.rows ?? [];
  const showTable = single(search.months) === "1";
  const sortedBuyers = sortRows(buyers, order, { ...figureValues, ...customerValues });

  return (
    <SalesFrame
      context={context}
      path={path}
      back={{ href: sectionHref(context, `${basePath}/products`), label: "All products" }}
      eyebrow={data?.group ?? "Product"}
      title={name}
      description={`Product ${key}${data?.group ? ` in the group ${data.group}` : ""}.`}
      fixed={["product"]}
      download={["rep", "customer"]}
      message={data?.product.message}
    >
      {!data ? (
        <Notice tone="error" title="The sales report could not be read">
          Try again in a few minutes.
        </Notice>
      ) : (
        <>
          <TileGrid min={210}>
            <StatTile label="Sales" value={short(data.product.current)} detail={against(data.product.current, data.product.previous)} />
            <StatTile label="Units" value={formatFigure(data.units.current)} detail={`${formatFigure(data.units.previous)} last year`} />
            <StatTile
              label="Customers"
              value={String(buyers.filter((row) => row.current > 0).length)}
              detail={`${buyers.filter((row) => row.current <= 0 && row.previous > 0).length} bought last year but not this period`}
            />
          </TileGrid>
          <Columns>
            <Panel title="Sales by month" description="This financial year against last year">
              <YearChart title={`Sales of ${name} by month`} months={data.year.months} showTable={showTable} tableHref={here(showTable ? {} : { months: "1" })} />
            </Panel>
            <Panel title="By rep" description="Sales and change against last year">
              <FigureBars label={`Sales of ${name} by rep`} rows={data.reps.rows} href={(row) => drillHref(context, `${basePath}/reps/${pathKey(row.key)}`, step)} />
            </Panel>
          </Columns>
          <Panel flush title={`${name} by ${breakdowns.find((item) => item.id === by)?.label.toLowerCase()}`} description="Click a row to go one level down. The product stays selected.">
            <TabNav label="Break down by" tabs={breakdownTabs(by, breakdowns, (item) => drillHref(context, path, undefined, { by: item }))} />
            {by === "customers" ? (
              <>
                <FigureTable
                  caption={`Customers who buy ${name}`}
                  nameHeader="Customer"
                  rows={sortedBuyers.slice(0, shown)}
                  order={order}
                  orderHref={(column, direction) => here({ order: `${column}:${direction}` })}
                  rowHref={(row) => drillHref(context, `${basePath}/customers/${pathKey(row.key)}`, step)}
                  meta={(row) => data.meta.get(row.key)}
                  extra={customerColumns((row) => data.repOf.get(row.key) ?? "No rep")}
                  extraValues={customerValues}
                />
                <MoreLink total={buyers.length} shown={shown} href={here({ shown: String(shown + 25) })} />
              </>
            ) : by === "reps" ? (
              <FigureTable
                caption={`Reps who sell ${name}`}
                nameHeader="Rep"
                rows={data.reps.rows}
                order={order}
                orderHref={(column, direction) => here({ order: `${column}:${direction}` })}
                rowHref={(row) => drillHref(context, `${basePath}/reps/${pathKey(row.key)}`, step)}
              />
            ) : (
              <FigureTable
                caption={`Customer groups that buy ${name}`}
                nameHeader="Customer group"
                rows={data.customerGroups.rows}
                order={order}
                orderHref={(column, direction) => here({ order: `${column}:${direction}` })}
                rowHref={(row) => drillHref(context, path, undefined, { by: "customers", customerGroup: row.key })}
              />
            )}
          </Panel>
        </>
      )}
    </SalesFrame>
  );
}
