import { notFound } from "next/navigation";
import {
  getSalesReportGrid,
  getSalesReportOptions,
  type SalesReportGrid,
  type SalesReportOptions,
  type SalesReportView,
} from "../../../../src/api/sales-report";
import { formatFigure, readOpen, reportQuery, toggleOpen, withYearTotals } from "../../../../src/sales-report";
import { ButtonLink, EmptyState, FilterChips, ListPage, Notice, PivotGrid, Stack, type PivotRow } from "../../../../src/ui";
import { basePath, freshness, hrefWith, requireReportUser, SalesFilters, SalesSections, type Search } from "../parts";

export const dynamic = "force-dynamic";

const views: Record<SalesReportView, { title: string; description: string }> = {
  "by-rep": {
    title: "Sales by rep",
    description: "Each rep, then the customer groups and the customers of the rep. The rep is the current rep of the customer.",
  },
  "by-product": {
    title: "Sales by product",
    description: "Each item group, then the products, then the customer groups or the reps that bought them.",
  },
  customers: {
    title: "Customers by rep",
    description: "The number of different customers with sales, for each rep and month. A total counts each customer once.",
  },
};

export async function generateMetadata({ params }: { params: Promise<{ view: string }> }) {
  const { view } = await params;
  return { title: views[view as SalesReportView]?.title ?? "Sales report" };
}

function isView(view: string): view is SalesReportView {
  return view in views;
}

/** The pivot views of the sales report: by rep, by product and customers. */
export default async function SalesGridPage({ params, searchParams }: { params: Promise<{ view: string }>; searchParams: Promise<Search> }) {
  const { view } = await params;
  if (!isView(view)) {
    notFound();
  }
  const path = `${basePath}/${view}`;
  await requireReportUser(path);
  const search = await searchParams;
  const open = readOpen(search.open);
  const query = reportQuery(search);

  let grid: SalesReportGrid | undefined;
  let message: string | undefined;
  let options: SalesReportOptions = { reps: [], provinces: [], itemGroups: [], channels: [] };
  let failed = false;
  try {
    let answer;
    [answer, options] = await Promise.all([getSalesReportGrid(view, query, open), getSalesReportOptions()]);
    ({ data: grid, message } = answer);
  } catch {
    failed = true;
  }

  // A count of distinct customers is not a sum, so the customers view has no year totals.
  const yearTotals = view !== "customers" && grid ? withYearTotals(grid.columns, grid.totals.values) : undefined;
  const cells = (values: number[]) => (yearTotals ? withYearTotals(grid!.columns, values).values : values).map(formatFigure);
  const rows: PivotRow[] = (grid?.rows ?? []).map((row) => ({
    key: row.key,
    level: row.level,
    label: row.label,
    values: cells(row.values),
    total: formatFigure(row.total),
    open: open.includes(row.key),
    toggleHref: row.hasChildren ? hrefWith(path, search, { open: toggleOpen(open, row.key).join(",") || undefined }) : undefined,
  }));
  const subtotalColumns = yearTotals ? yearTotals.headers.flatMap((header, index) => (header.startsWith("FY") ? [index] : [])) : [];
  const measure = search.measure === "quantity" ? "quantity" : "amount";
  const exportQuery = reportQuery(search);
  exportQuery.set("view", view);

  return (
    <ListPage
      header={{
        eyebrow: "Sales report",
        title: views[view].title,
        description: views[view].description,
        actions: grid ? (
          <ButtonLink href={`${basePath}/export?${exportQuery}`} variant="secondary">
            Download as CSV
          </ButtonLink>
        ) : undefined,
      }}
      sections={<SalesSections current={path} search={search} />}
      notice={
        failed ? (
          <Notice tone="error" title="The sales report could not be read">
            Try again in a few minutes.
          </Notice>
        ) : message ? (
          <Notice tone="warning" title="The filters are not correct">
            {message}
          </Notice>
        ) : grid ? (
          <p>{freshness(grid.dataTo)}</p>
        ) : undefined
      }
      filters={
        <Stack gap="s">
          <SalesFilters path={path} search={search} options={options} keep={["measure", "then"]} />
          {view !== "customers" ? (
            <FilterChips
              label="Show the amount or the quantity"
              chips={[
                { label: "Amount (R)", href: hrefWith(path, search, { measure: undefined }), current: measure === "amount" },
                { label: "Quantity", href: hrefWith(path, search, { measure: "quantity" }), current: measure === "quantity" },
              ]}
            />
          ) : null}
          {view === "by-product" ? (
            <FilterChips
              label="Show the products by customer group or by rep"
              chips={[
                { label: "Then customer group", href: hrefWith(path, search, { then: undefined, open: undefined }), current: search.then !== "rep" },
                { label: "Then rep", href: hrefWith(path, search, { then: "rep", open: undefined }), current: search.then === "rep" },
              ]}
            />
          ) : null}
        </Stack>
      }
      table={
        grid ? (
          <PivotGrid
            caption={views[view].title}
            rowHeader={grid.levelNames.join(", ")}
            groups={yearTotals?.yearHeaders}
            headers={yearTotals?.headers ?? grid.columns.map((column) => column.label)}
            subtotalColumns={subtotalColumns}
            rows={rows}
            totals={{ values: cells(grid.totals.values), total: formatFigure(grid.totals.total) }}
            empty={<EmptyState title="No sales for these filters" description="Choose other financial years, or clear the filters." />}
          />
        ) : null
      }
    />
  );
}
