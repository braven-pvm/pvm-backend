import { getPivot } from "../../../src/api/sales-report";
import {
  addMonths,
  apiQuery,
  financialYearStart,
  monthOf,
  monthsSinceLast,
  statusOf,
  type CustomerStatus,
  type Dimension,
  type Measure,
  type ReportState,
} from "../../../src/sales-report";

/*
 * The data of the prototype pages, from the pivot API. Each function asks for one grouping,
 * and the pages call them in parallel. The pages ask for few groupings: each one reads two
 * years of lines. A tree call (open=*) gives several levels in one request.
 * Refer to docs/design/sales-report-pages.md.
 */

export type Figure = { key: string; label: string; current: number; previous: number };

export type Grouped = { rows: Figure[]; current: number; previous: number; message?: string };

function query(state: ReportState, change: Partial<ReportState>) {
  return apiQuery({ ...state, open: [], sort: "value", compare: true, ...change });
}

/** The sales of each value of a dimension in the period, against the same months a year before. */
export async function byDimension(state: ReportState, dimension: Dimension, measure: Measure = "amount"): Promise<Grouped> {
  const answer = await getPivot(query(state, { rows: [dimension], columns: "none", measure }));
  const pivot = answer.data;
  if (!pivot) {
    return { rows: [], current: 0, previous: 0, message: answer.message };
  }
  return {
    rows: pivot.rows.map((row) => ({ key: row.key, label: row.label, current: row.total, previous: row.previousTotal ?? 0 })),
    current: pivot.totals.total,
    previous: pivot.totals.previousTotal ?? 0,
  };
}

/** The total only. */
export async function total(state: ReportState, measure: Measure = "amount") {
  const grouped = await byDimension(state, "itemGroup", measure);
  return { current: grouped.current, previous: grouped.previous };
}

/** The sales of each value of a dimension in the last 12 months, this month last, for sparklines and the status. */
export async function lastTwelveMonths(state: ReportState, dimension: Dimension, today = new Date()): Promise<Map<string, number[]>> {
  const to = monthOf(today);
  const answer = await getPivot(query(state, { rows: [dimension], columns: "months", from: addMonths(to, -11), to, compare: false }));
  return new Map((answer.data?.rows ?? []).map((row) => [row.key, row.values]));
}

/** The months of this financial year against last year, for the chart and the month table. */
export async function financialYear(state: ReportState, today = new Date()) {
  const from = financialYearStart(today);
  const answer = await getPivot(query(state, { rows: ["itemGroup"], columns: "months", from, to: addMonths(from, 11) }));
  const pivot = answer.data;
  const thisMonth = monthOf(today);
  return {
    months: (pivot?.columns ?? []).map((column, index) => ({
      label: column.label,
      /** A month after this month has no sales yet. */
      future: addMonths(from, index) > thisMonth,
      current: pivot?.totals.values[index] ?? 0,
      previous: pivot?.totals.previous?.[index] ?? 0,
    })),
    dataTo: pivot?.dataTo,
  };
}

/**
 * Two dimensions in one call, such as product groups and their products: the parents, the
 * children, and the parent of each child.
 */
export async function twoLevels(state: ReportState, parent: Dimension, child: Dimension, measure: Measure = "amount") {
  const answer = await getPivot(query(state, { rows: [parent, child], columns: "none", measure, open: ["*"] }));
  const parents: Figure[] = [];
  const children: Figure[] = [];
  const parentOf = new Map<string, string>();
  const labels = new Map<string, string>();
  for (const row of answer.data?.rows ?? []) {
    const figure = { key: row.key, label: row.label, current: row.total, previous: row.previousTotal ?? 0 };
    if (row.level === 0) {
      parents.push(figure);
      labels.set(row.key, row.label);
    } else {
      const at = row.key.indexOf("|");
      const childKey = row.key.slice(at + 1);
      children.push({ ...figure, key: childKey });
      parentOf.set(childKey, row.key.slice(0, at));
    }
  }
  return { parents, children, parentOf, parentLabel: (key: string) => labels.get(parentOf.get(key) ?? "") ?? "", message: answer.message };
}

export type CustomerFigure = Figure & { status: CustomerStatus; monthsSinceOrder: number | null; lastTwelve: number[] };

export type CustomerBundle = {
  /** The customers with sales in the period or a year before, each with the status of the prototype. */
  rows: CustomerFigure[];
  /** The reps, with the sales of their customers. */
  reps: Figure[];
  /** The key of the rep of each customer. */
  repOf: Map<string, string>;
  /** "Pharmacy · Rep A" for each customer. */
  meta: Map<string, string>;
  /** The sales of the last 12 months of each rep: the sum of its customers. */
  repMonths: Map<string, number[]>;
  current: number;
  previous: number;
  message?: string;
};

/**
 * The customers, their reps and their channels in two calls: a tree of rep, channel and
 * customer (one customer has one rep and one channel), and the last 12 months by customer.
 */
export async function customerBundle(state: ReportState, today = new Date()): Promise<CustomerBundle> {
  const [answer, months] = await Promise.all([
    getPivot(query(state, { rows: ["rep", "channel", "customer"], columns: "none", open: ["*"] })),
    lastTwelveMonths(state, "customer", today),
  ]);
  const pivot = answer.data;
  const reps: Figure[] = [];
  const repLabels = new Map<string, string>();
  const channelLabels = new Map<string, string>();
  const rows: CustomerFigure[] = [];
  const repOf = new Map<string, string>();
  const meta = new Map<string, string>();
  const repMonths = new Map<string, number[]>();
  for (const row of pivot?.rows ?? []) {
    const parts = row.key.split("|");
    if (row.level === 0) {
      reps.push({ key: row.key, label: row.label, current: row.total, previous: row.previousTotal ?? 0 });
      repLabels.set(row.key, row.label);
    } else if (row.level === 1) {
      channelLabels.set(row.key, row.label);
    } else if (row.total !== 0 || (row.previousTotal ?? 0) !== 0) {
      const key = parts[2];
      const lastTwelve = months.get(key) ?? [];
      const monthsSinceOrder = monthsSinceLast(lastTwelve);
      const current = row.total;
      const previous = row.previousTotal ?? 0;
      rows.push({ key, label: row.label, current, previous, lastTwelve, monthsSinceOrder, status: statusOf(current, previous, monthsSinceOrder) });
      repOf.set(key, parts[0]);
      meta.set(key, [channelLabels.get(`${parts[0]}|${parts[1]}`), repLabels.get(parts[0])].filter(Boolean).join(" · "));
      const sum = repMonths.get(parts[0]) ?? new Array<number>(lastTwelve.length).fill(0);
      lastTwelve.forEach((value, index) => (sum[index] = (sum[index] ?? 0) + value));
      repMonths.set(parts[0], sum);
    }
  }
  return {
    rows,
    reps,
    repOf,
    meta,
    repMonths,
    current: pivot?.totals.total ?? 0,
    previous: pivot?.totals.previousTotal ?? 0,
    message: answer.message,
  };
}
