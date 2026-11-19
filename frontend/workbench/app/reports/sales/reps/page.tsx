import { Notice, Panel } from "../../../../src/ui";
import { byDimension, customers, lastTwelveMonths, parentOf } from "../data";
import { drillHref, readContext, SalesFrame, sectionHref } from "../frame";
import { basePath, requireReportUser, type Search } from "../parts";
import { customerCounts, FigureTable, pathKey, rand, readOrder, sparkColumn, StatusPill } from "../views";
import { Delta } from "../../../../src/ui";

export const dynamic = "force-dynamic";
export const metadata = { title: "Reps · Sales report" };

const path = `${basePath}/reps`;

/** Each rep's sales against last year, with the customers who buy and the customers to call. */
export default async function SalesRepsPage({ searchParams }: { searchParams: Promise<Search> }) {
  await requireReportUser(path);
  const search = await searchParams;
  const context = await readContext(search);
  const { state } = context;

  let data;
  try {
    const [reps, months, customerRows] = await Promise.all([byDimension(state, "rep"), lastTwelveMonths(state, "rep"), customers(state)]);
    const repOf = await parentOf(state, "rep", "customer", reps.rows, "key");
    data = { reps, months, counts: customerCounts(customerRows.rows, repOf) };
  } catch {
    data = undefined;
  }

  const order = readOrder(search, { key: "sales", direction: "desc" });
  const count = (key: string) => data?.counts.get(key) ?? { active: 0, fresh: 0, toCall: 0, growing: 0 };

  return (
    <SalesFrame
      context={context}
      path={path}
      tab="Reps"
      eyebrow="Sales report"
      title="Reps"
      description="Each rep's sales against last year, with the customers who buy and the customers to call."
      fixed={["rep"]}
      download={["rep", "customer"]}
      message={data?.reps.message}
    >
      {!data ? (
        <Notice tone="error" title="The sales report could not be read">
          Try again in a few minutes.
        </Notice>
      ) : (
        <Panel
          flush
          title="All reps"
          description={
            <>
              Total {rand(data.reps.current)} <Delta current={data.reps.current} previous={data.reps.previous} />
            </>
          }
        >
          <FigureTable
            caption="Sales by rep"
            nameHeader="Rep"
            rows={data.reps.rows}
            order={order}
            orderHref={(key, direction) => sectionHref(context, path, { order: `${key}:${direction}` })}
            rowHref={(row) => drillHref(context, `${path}/${pathKey(row.key)}`)}
            extra={[
              {
                key: "active",
                header: "Active customers",
                align: "end",
                sortable: true,
                render: (row) => `${count(row.key).active}${count(row.key).fresh ? ` (${count(row.key).fresh} new)` : ""}`,
              },
              {
                key: "call",
                header: "To call",
                align: "end",
                sortable: true,
                render: (row) => (count(row.key).toCall ? <StatusPill status="At risk" label={String(count(row.key).toCall)} /> : "0"),
              },
              sparkColumn((row) => data.months.get(row.key) ?? []),
            ]}
            extraValues={{ active: (row) => count(row.key).active, call: (row) => count(row.key).toCall }}
          />
        </Panel>
      )}
    </SalesFrame>
  );
}
