import type { ReactNode } from "react";
import { formatFigure, type CustomerStatus } from "../../../src/sales-report";
import {
  BarList,
  ButtonLink,
  ColumnChart,
  DataTable,
  Delta,
  EmptyState,
  LinkList,
  PivotGrid,
  Sparkline,
  Stack,
  StatusBadge,
  type Column,
  type SortDirection,
  type StatusTone,
} from "../../../src/ui";
import type { CustomerFigure, Figure } from "./data";
import { single, type Search } from "./parts";

/*
 * The parts of the prototype views, with real data. Refer to the prototype at
 * https://claude.ai/artifact/8N9HSbu6wkYaGA1GY6VB9H and docs/design/sales-report-pages.md.
 */

/** "Last order this month", "Last order 3 months ago", "No order in 12 months". */
export function lastOrderText(monthsSinceOrder: number | null) {
  if (monthsSinceOrder === null) {
    return "No order in 12 months";
  }
  return monthsSinceOrder === 0 ? "Last order this month" : monthsSinceOrder === 1 ? "Last order last month" : `Last order ${monthsSinceOrder} months ago`;
}

/** "R 1 234 567". */
export function rand(value: number) {
  return `${value < 0 ? "-" : ""}R ${formatFigure(Math.abs(value))}`;
}

/** "R 1.23 m", "R 850 k". */
export function short(value: number) {
  const size = Math.abs(value);
  if (size >= 1_000_000) {
    return `R ${(value / 1_000_000).toFixed(2)} m`;
  }
  if (size >= 1_000) {
    return `R ${Math.round(value / 1_000)} k`;
  }
  return rand(value);
}

/** "▲ 12.3% against R 1.1 m last year". */
export function against(current: number, previous: number): ReactNode {
  return (
    <>
      <Delta current={current} previous={previous} /> against {short(previous)} last year
    </>
  );
}

const statusTones: Record<CustomerStatus, StatusTone> = {
  Growing: "success",
  Steady: "neutral",
  Declining: "error",
  New: "info",
  "At risk": "warning",
  Lost: "error",
};

export function StatusPill({ status, label }: { status: CustomerStatus; label?: string }) {
  return <StatusBadge status={status} tone={statusTones[status]} label={label ?? status} />;
}

/** The months of this financial year against last year, and the table of the months under it. */
export function YearChart({
  title,
  months,
  showTable,
  tableHref,
}: {
  title: string;
  months: { label: string; future: boolean; current: number; previous: number }[];
  showTable: boolean;
  tableHref: string;
}) {
  if (months.length === 0) {
    return <EmptyState title="No sales by month" />;
  }
  // The financial year carries the calendar year of its last month, February.
  const thisYear = `FY${Number(months[months.length - 1].label.slice(-4))}`;
  const lastYear = `FY${Number(months[months.length - 1].label.slice(-4)) - 1}`;
  const past = months.filter((month) => !month.future);
  const sumCurrent = past.reduce((sum, month) => sum + month.current, 0);
  const sumPrevious = past.reduce((sum, month) => sum + month.previous, 0);
  return (
    <Stack gap="s">
      <ColumnChart
        label={title}
        categories={months.map((month) => month.label.slice(0, 3))}
        series={[
          { name: `${thisYear} (${months[0].label} to ${months[months.length - 1].label})`, values: months.map((month) => (month.future ? 0 : month.current)) },
          { name: lastYear, values: months.map((month) => month.previous) },
        ]}
        format={short}
      />
      <div>
        <ButtonLink href={tableHref} variant="tertiary">
          {showTable ? "Hide month by month" : "Show month by month"}
        </ButtonLink>
      </div>
      {showTable ? (
        <PivotGrid
          caption={`${title}, month by month`}
          rowHeader="Year"
          headers={months.map((month) => month.label.slice(0, 3))}
          rows={[
            { key: "this", level: 0, label: thisYear, values: months.map((month) => (month.future ? "" : formatFigure(month.current))), total: formatFigure(sumCurrent) },
            {
              key: "last",
              level: 0,
              label: lastYear,
              values: months.map((month) => formatFigure(month.previous)),
              total: formatFigure(months.reduce((sum, month) => sum + month.previous, 0)),
            },
          ]}
          totals={{
            label: "Change",
            values: months.map((month) => (month.future ? "" : formatChangeText(month.current, month.previous))),
            total: formatChangeText(sumCurrent, sumPrevious),
          }}
          empty={<EmptyState title="No sales by month" />}
        />
      ) : null}
    </Stack>
  );
}

function formatChangeText(current: number, previous: number) {
  if (previous <= 0) {
    return current > 0 ? "New" : "–";
  }
  const percent = ((current - previous) / previous) * 100;
  return `${percent > 0 ? "▲" : percent < 0 ? "▼" : ""} ${Math.abs(percent).toFixed(1)}%`;
}

/** The sort of a table from the URL parameter "order", such as "sales:desc". */
export function readOrder(search: Search, fallback: { key: string; direction: SortDirection }) {
  const [key, direction] = (single(search.order) ?? "").split(":");
  return key ? { key, direction: direction === "asc" ? ("asc" as const) : ("desc" as const) } : fallback;
}

export function sortRows<Row>(rows: Row[], order: { key: string; direction: SortDirection }, values: Record<string, (row: Row) => number | string>) {
  const value = values[order.key];
  if (!value) {
    return rows;
  }
  const sign = order.direction === "asc" ? 1 : -1;
  return [...rows].sort((a, b) => {
    const x = value(a);
    const y = value(b);
    return (typeof x === "string" && typeof y === "string" ? x.localeCompare(y) : Number(x) - Number(y)) * sign;
  });
}

const changeOf = (row: Figure) => (row.previous > 0 ? (row.current - row.previous) / row.previous : row.current > 0 ? 9 : 0);

/** The sort values of the figure columns. */
export const figureValues: Record<string, (row: Figure) => number | string> = {
  name: (row) => row.label,
  sales: (row) => row.current,
  last: (row) => row.previous,
  change: changeOf,
};

/** A table of figures, as the prototype tables: name, sales, last year, change, then extra columns. */
export function FigureTable<Row extends Figure>({
  caption,
  nameHeader,
  rows,
  order,
  orderHref,
  rowHref,
  meta,
  extra = [],
  extraValues = {},
  hideLast = false,
  empty,
}: {
  caption: string;
  nameHeader: string;
  rows: Row[];
  order: { key: string; direction: SortDirection };
  orderHref: (key: string, direction: SortDirection) => string;
  rowHref?: (row: Row) => string;
  meta?: (row: Row) => string | undefined;
  extra?: Column<Row>[];
  extraValues?: Record<string, (row: Row) => number | string>;
  /** Leave out the "Last year" column, for a narrow panel. */
  hideLast?: boolean;
  empty?: ReactNode;
}) {
  const sorted = sortRows(rows, order, { ...figureValues, ...extraValues });
  const columns: Column<Row>[] = [
    {
      key: "name",
      header: nameHeader,
      sortable: true,
      render: (row) => (
        <>
          {row.label}
          {meta?.(row) ? (
            <>
              <br />
              <small>{meta(row)}</small>
            </>
          ) : null}
        </>
      ),
    },
    { key: "sales", header: "Sales", align: "end", sortable: true, render: (row) => rand(row.current) },
    ...(hideLast ? [] : [{ key: "last", header: "Last year", align: "end", sortable: true, render: (row: Row) => rand(row.previous) } as Column<Row>]),
    { key: "change", header: "Change", align: "end", sortable: true, render: (row) => <Delta current={row.current} previous={row.previous} /> },
    ...extra,
  ];
  return (
    <DataTable
      caption={caption}
      columns={columns}
      rows={sorted}
      rowKey={(row) => row.key || "none"}
      rowHref={rowHref}
      sort={order}
      sortHref={orderHref}
      empty={empty ?? <EmptyState title="Nothing matches these filters" description="Choose another period, or clear a filter." />}
    />
  );
}

export function sparkColumn(values: (row: Figure) => number[]): Column<Figure> {
  return { key: "trend", header: "Last 12 months", render: (row) => <Sparkline values={values(row)} label="Sales of the last 12 months" /> };
}

/** Product groups, or reps, as bars with the change against last year. */
export function FigureBars({ label, rows, href }: { label: string; rows: Figure[]; href?: (row: Figure) => string }) {
  const shown = rows.filter((row) => row.current > 0).sort((a, b) => b.current - a.current).slice(0, 12);
  if (shown.length === 0) {
    return <EmptyState title="No sales" description="Choose another period, or clear a filter." />;
  }
  return (
    <BarList
      label={label}
      max={Math.max(1, ...shown.map((row) => row.current))}
      bars={shown.map((row) => ({
        label: row.label,
        value: row.current,
        display: short(row.current),
        change: <Delta current={row.current} previous={row.previous} />,
        href: href?.(row),
      }))}
    />
  );
}

/** The customers to call: at risk or lost, the largest sales a year before first. */
export function CallList({ rows, href, meta, limit = 6 }: { rows: CustomerFigure[]; href: (row: CustomerFigure) => string; meta: (row: CustomerFigure) => string; limit?: number }) {
  const calls = rows
    .filter((row) => row.status === "At risk" || row.status === "Lost")
    .sort((a, b) => b.previous - a.previous)
    .slice(0, limit);
  if (calls.length === 0) {
    return <EmptyState title="Every customer ordered in the last 2 months" />;
  }
  return (
    <LinkList
      label="Customers to call"
      items={calls.map((row) => ({
        id: row.key,
        title: row.label,
        meta: meta(row),
        href: href(row),
        aside: (
          <>
            <StatusPill status={row.status} />
            {short(row.previous)} last year
          </>
        ),
      }))}
    />
  );
}

/** The customers with the biggest growth or drop against last year. */
export function MoverList({ rows, href, meta, direction }: { rows: Figure[]; href: (row: Figure) => string; meta: (row: Figure) => string; direction: "up" | "down" }) {
  const movers = rows
    .map((row) => ({ row, change: row.current - row.previous }))
    .filter((item) => (direction === "up" ? item.change > 0 : item.change < 0))
    .sort((a, b) => (direction === "up" ? b.change - a.change : a.change - b.change))
    .slice(0, 5);
  if (movers.length === 0) {
    return <EmptyState title="No changes" />;
  }
  return (
    <LinkList
      label={direction === "up" ? "Biggest growth" : "Biggest drops"}
      items={movers.map(({ row, change }) => ({
        id: row.key,
        title: row.label,
        meta: meta(row),
        href: href(row),
        aside: (
          <>
            <strong>{`${change > 0 ? "+" : "-"}${short(Math.abs(change))}`}</strong>
            <Delta current={row.current} previous={row.previous} />
          </>
        ),
      }))}
    />
  );
}

/** "Show 25 more (40 left)". */
export function MoreLink({ total, shown, href }: { total: number; shown: number; href: string }) {
  return total > shown ? (
    <Stack direction="row" justify="end" gap="xs">
      <ButtonLink href={href}>{`Show 25 more (${total - shown} left)`}</ButtonLink>
    </Stack>
  ) : null;
}

/** The key of a detail page from its path. "none" is the empty key, such as "No rep". */
export function keyOf(id: string) {
  const key = decodeURIComponent(id);
  return key === "none" ? "" : key;
}

export function pathKey(key: string) {
  return encodeURIComponent(key || "none");
}

/** Counts of the customers of each parent, such as each rep: active, new and to call. */
export function customerCounts(rows: CustomerFigure[], parentOf: Map<string, string>) {
  const counts = new Map<string, { active: number; fresh: number; toCall: number; growing: number }>();
  for (const row of rows) {
    const parent = parentOf.get(row.key) ?? "";
    const count = counts.get(parent) ?? { active: 0, fresh: 0, toCall: 0, growing: 0 };
    count.active += row.current > 0 ? 1 : 0;
    count.fresh += row.status === "New" ? 1 : 0;
    count.toCall += row.status === "At risk" || row.status === "Lost" ? 1 : 0;
    count.growing += row.status === "Growing" ? 1 : 0;
    counts.set(parent, count);
  }
  return counts;
}

/** The tabs of a breakdown on a detail page, such as Customers, Products and Product groups. */
export function breakdownTabs(current: string, options: { id: string; label: string }[], href: (id: string) => string) {
  return options.map((option) => ({ label: option.label, href: href(option.id), current: option.id === current }));
}

/** The customer columns of the prototype after the figures: an optional rep, the last order and the status. */
export function customerColumns<Row extends CustomerFigure>(rep?: (row: Row) => string): Column<Row>[] {
  return [
    ...(rep ? [{ key: "rep", header: "Rep", render: rep, weak: true } as Column<Row>] : []),
    { key: "order", header: "Last order", sortable: true, render: (row) => lastOrderShort(row.monthsSinceOrder) },
    { key: "status", header: "Status", sortable: true, render: (row) => <StatusPill status={row.status} /> },
  ];
}

export const customerValues = {
  order: (row: CustomerFigure) => -(row.monthsSinceOrder ?? 99),
  status: (row: CustomerFigure) => ["Lost", "At risk", "Declining", "Steady", "New", "Growing"].indexOf(row.status),
};

function lastOrderShort(monthsSinceOrder: number | null) {
  if (monthsSinceOrder === null) {
    return "Over 12 months";
  }
  return monthsSinceOrder === 0 ? "This month" : monthsSinceOrder === 1 ? "Last month" : `${monthsSinceOrder} months ago`;
}
