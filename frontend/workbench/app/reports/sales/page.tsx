import { ButtonLink, Columns, Delta, Notice, Panel, StatTile, TileGrid } from "../../../src/ui";
import { byDimension, customerMeta, customers, financialYear, lastTwelveMonths, total } from "./data";
import { drillHref, readContext, SalesFrame, sectionHref } from "./frame";
import { basePath, requireReportUser, single, type Search } from "./parts";
import { against, CallList, lastOrderText, FigureBars, FigureTable, MoverList, readOrder, short, sparkColumn, YearChart } from "./views";
import { formatFigure } from "../../../src/sales-report";

export const dynamic = "force-dynamic";
export const metadata = { title: "Sales report" };

/** The overview of the prototype: how sales go against last year, with real data. */
export default async function SalesOverviewPage({ searchParams }: { searchParams: Promise<Search> }) {
  await requireReportUser(basePath);
  const search = await searchParams;
  const context = await readContext(search);
  const { state } = context;

  let data;
  try {
    const [reps, repMonths, groups, customerRows, units, year] = await Promise.all([
      byDimension(state, "rep"),
      lastTwelveMonths(state, "rep"),
      byDimension(state, "itemGroup"),
      customers(state),
      total(state, "quantity"),
      financialYear(state),
    ]);
    const meta = await customerMeta(state, reps.rows);
    data = { reps, repMonths, groups, customerRows, units, year, meta };
  } catch {
    data = undefined;
  }

  const order = readOrder(search, { key: "sales", direction: "desc" });
  const here = (extra?: Record<string, string>) => sectionHref(context, basePath, extra);
  const active = data ? data.customerRows.rows.filter((row) => row.current > 0).length : 0;
  const activeBefore = data ? data.customerRows.rows.filter((row) => row.previous > 0).length : 0;
  const fresh = data ? data.customerRows.rows.filter((row) => row.status === "New").length : 0;
  const toCall = data ? data.customerRows.rows.filter((row) => row.status === "At risk" || row.status === "Lost").length : 0;
  const showTable = single(search.months) === "1";

  return (
    <SalesFrame
      context={context}
      path={basePath}
      tab="Overview"
      eyebrow="Reports"
      title={context.current ? `Sales report: ${context.current.name}` : "Sales report"}
      description="How sales go against last year, by rep, customer and product. Start here, then click through to the detail."
      download={["rep", "customer"]}
      message={data?.reps.message}
    >
      {!data ? (
        <Notice tone="error" title="The sales report could not be read">
          Try again in a few minutes.
        </Notice>
      ) : (
        <>
          <TileGrid min={210}>
            <StatTile label="Sales" value={short(data.reps.current)} detail={against(data.reps.current, data.reps.previous)} />
            <StatTile label="Units sold" value={formatFigure(data.units.current)} detail={(state.include.itemGroup ?? []).length > 0 ? "The selected product groups" : "All products"} />
            <StatTile
              label="Active customers"
              value={String(active)}
              detail={
                <>
                  <Delta current={active} previous={activeBefore} /> against {activeBefore} last year · {fresh} new
                </>
              }
              href={sectionHref(context, `${basePath}/customers`)}
            />
            <StatTile label="Customers to call" value={String(toCall)} detail="No order in 2 months or more" href={sectionHref(context, `${basePath}/customers`, { status: "call" })} />
          </TileGrid>

          <Columns>
            <Panel title="Sales by month" description="This financial year against last year">
              <YearChart title="Sales by month" months={data.year.months} showTable={showTable} tableHref={here(showTable ? {} : { months: "1" })} />
            </Panel>
            <Panel title="Customers to call" description="No order in 2 months or more, the biggest customers first">
              <CallList
                rows={data.customerRows.rows}
                href={(row) => drillHref(context, `${basePath}/customers/${encodeURIComponent(row.key)}`)}
                meta={(row) => [data.meta.get(row.key), lastOrderText(row.monthsSinceOrder)].filter(Boolean).join(" · ")}
              />
            </Panel>
          </Columns>

          <Columns>
            <Panel flush title="Reps" actions={
                <ButtonLink variant="tertiary" href={sectionHref(context, `${basePath}/reps`)}>
                  All reps
                </ButtonLink>
              }>
              <FigureTable
                caption="Sales by rep"
                nameHeader="Rep"
                rows={data.reps.rows}
                order={order}
                orderHref={(key, direction) => here({ order: `${key}:${direction}` })}
                rowHref={(row) => drillHref(context, `${basePath}/reps/${encodeURIComponent(row.key || "none")}`)}
                extra={[sparkColumn((row) => data.repMonths.get(row.key) ?? [])]}
                hideLast
              />
            </Panel>
            <Panel title="Product groups" description="Sales and change against last year">
              <FigureBars label="Sales by product group" rows={data.groups.rows} href={(row) => sectionHref(context, `${basePath}/products`, { itemGroup: row.key })} />
            </Panel>
          </Columns>

          <Columns>
            <Panel title="Biggest growth" description="Customers, against last year">
              <MoverList
                direction="up"
                rows={data.customerRows.rows}
                href={(row) => drillHref(context, `${basePath}/customers/${encodeURIComponent(row.key)}`)}
                meta={(row) => data.meta.get(row.key) || "Customer"}
              />
            </Panel>
            <Panel title="Biggest drops" description="Customers, against last year">
              <MoverList
                direction="down"
                rows={data.customerRows.rows}
                href={(row) => drillHref(context, `${basePath}/customers/${encodeURIComponent(row.key)}`)}
                meta={(row) => data.meta.get(row.key) || "Customer"}
              />
            </Panel>
          </Columns>
        </>
      )}
    </SalesFrame>
  );
}
