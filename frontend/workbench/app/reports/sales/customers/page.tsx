import { customerStatuses, type CustomerStatus } from "../../../../src/sales-report";
import { FilterBar as SearchBar, FilterChips, Notice, Panel, TextField } from "../../../../src/ui";
import { byDimension, customerMeta, customers } from "../data";
import { drillHref, readContext, SalesFrame, sectionHref } from "../frame";
import { basePath, requireReportUser, single, type Search } from "../parts";
import { customerColumns, customerValues, FigureTable, figureValues, MoreLink, pathKey, readOrder, sortRows } from "../views";

export const dynamic = "force-dynamic";
export const metadata = { title: "Customers · Sales report" };

const path = `${basePath}/customers`;

/** Every customer with sales this period or last year. A status shows who grows, who drops and who stopped ordering. */
export default async function SalesCustomersPage({ searchParams }: { searchParams: Promise<Search> }) {
  await requireReportUser(path);
  const search = await searchParams;
  const context = await readContext(search);
  const { state } = context;
  const q = (single(search.q) ?? "").trim();
  const status = single(search.status) ?? "";
  const shown = Math.max(25, Number.parseInt(single(search.shown) ?? "25", 10) || 25);

  let data;
  try {
    const [rows, reps] = await Promise.all([customers(state), byDimension(state, "rep")]);
    data = { rows, meta: await customerMeta(state, reps.rows) };
  } catch {
    data = undefined;
  }

  const all = data?.rows.rows ?? [];
  const isCall = (value: CustomerStatus) => value === "At risk" || value === "Lost";
  const filtered = all.filter(
    (row) =>
      (!q || row.label.toLowerCase().includes(q.toLowerCase()) || row.key.toLowerCase().includes(q.toLowerCase())) &&
      (!status || (status === "call" ? isCall(row.status) : row.status === status)),
  );
  const order = readOrder(search, { key: "sales", direction: "desc" });
  const keep = (extra: Record<string, string>) => {
    const values: Record<string, string> = {};
    if (q) values.q = q;
    if (status) values.status = status;
    if (single(search.order)) values.order = single(search.order)!;
    return sectionHref(context, path, { ...values, ...extra });
  };
  const hidden = Object.fromEntries(new URL(keep({}), "http://local").searchParams);
  delete hidden.q;

  return (
    <SalesFrame
      context={context}
      path={path}
      tab="Customers"
      eyebrow="Sales report"
      title="Customers"
      description="Every customer with sales this period or last year. A status shows who grows, who drops and who stopped ordering."
      download={["customer", "product"]}
      message={data?.rows.message}
    >
      {!data ? (
        <Notice tone="error" title="The sales report could not be read">
          Try again in a few minutes.
        </Notice>
      ) : (
        <>
          <SearchBar action={path} label="Search the customers" keep={hidden} activeCount={q ? 1 : 0}>
            <TextField name="q" type="search" label="Search" placeholder="Customer name or ID" defaultValue={q} showOptional={false} />
          </SearchBar>
          <FilterChips
            label="Status"
            chips={[
              { label: "All", count: all.length, href: keep({ status: "" }), current: !status },
              { label: "To call", count: all.filter((row) => isCall(row.status)).length, href: keep({ status: "call" }), current: status === "call" },
              ...customerStatuses.map((value) => ({ label: value, count: all.filter((row) => row.status === value).length, href: keep({ status: value }), current: status === value })),
            ]}
          />
          <Panel flush title="Customers" description={`${filtered.length} shown`}>
            <FigureTable
              caption="Customers"
              nameHeader="Customer"
              rows={sortRows(filtered, order, { ...figureValues, ...customerValues }).slice(0, shown)}
              order={order}
              orderHref={(key, direction) => keep({ order: `${key}:${direction}` })}
              rowHref={(row) => drillHref(context, `${path}/${pathKey(row.key)}`)}
              meta={(row) => data.meta.get(row.key)}
              extra={customerColumns()}
              extraValues={customerValues}
            />
            <MoreLink total={filtered.length} shown={shown} href={keep({ shown: String(shown + 25) })} />
          </Panel>
          <small>
            Status: Growing is up more than 10%, Declining is down more than 10%. At risk has no order in the last 2 months. Lost has no order in the last 4 months. New had no sales last
            year.
          </small>
        </>
      )}
    </SalesFrame>
  );
}
