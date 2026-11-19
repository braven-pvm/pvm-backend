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
 * The data of the prototype pages, from the pivot API. Each function asks for one
 * grouping. The pages call them in parallel. Refer to docs/design/sales-report-pages.md.
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
  const grouped = await byDimension({ ...state, include: state.include }, "rep", measure);
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
  const answer = await getPivot(query(state, { rows: ["rep"], columns: "months", from, to: addMonths(from, 11) }));
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

export type CustomerFigure = Figure & { status: CustomerStatus; monthsSinceOrder: number | null; lastTwelve: number[] };

/** The customers with sales in the period or a year before, each with the status of the prototype. */
export async function customers(state: ReportState, today = new Date()): Promise<{ rows: CustomerFigure[]; current: number; previous: number; message?: string }> {
  const [grouped, months] = await Promise.all([byDimension(state, "customer"), lastTwelveMonths(state, "customer", today)]);
  const rows = grouped.rows
    .filter((row) => row.current !== 0 || row.previous !== 0)
    .map((row) => {
      const lastTwelve = months.get(row.key) ?? [];
      const monthsSinceOrder = monthsSinceLast(lastTwelve);
      return { ...row, lastTwelve, monthsSinceOrder, status: statusOf(row.current, row.previous, monthsSinceOrder) };
    });
  return { rows, current: grouped.current, previous: grouped.previous, message: grouped.message };
}

/** The label of one key of a dimension in the filter, such as the name of a rep. */
export async function labelOf(state: ReportState, dimension: Dimension, key: string) {
  const grouped = await byDimension({ ...state, include: { ...state.include, [dimension]: [key] } }, dimension);
  return grouped.rows.find((row) => row.key === key)?.label;
}

/**
 * The label of the parent of each child key, such as the rep of each customer. The pivot opens
 * each parent and gives its children. A parent with an empty key, such as "No rep", stays out.
 */
export async function parentOf(state: ReportState, parent: Dimension, child: Dimension, parents: Figure[], use: "label" | "key" = "label"): Promise<Map<string, string>> {
  const keys = parents.map((row) => row.key).filter((key) => key.length > 0);
  if (keys.length === 0) {
    return new Map();
  }
  const answer = await getPivot(query(state, { rows: [parent, child], columns: "none", open: keys }));
  const labels = new Map(parents.map((row) => [row.key, row.label]));
  const result = new Map<string, string>();
  for (const row of answer.data?.rows ?? []) {
    if (row.level === 1) {
      const [parentKey, childKey] = [row.key.slice(0, row.key.indexOf("|")), row.key.slice(row.key.indexOf("|") + 1)];
      result.set(childKey, use === "key" ? parentKey : (labels.get(parentKey) ?? parentKey));
    }
  }
  return result;
}

/** "Pharmacy · Rep A" for each customer. */
export async function customerMeta(state: ReportState, reps: Figure[]): Promise<Map<string, string>> {
  const channels = await byDimension(state, "channel");
  const [repOf, channelOf] = await Promise.all([parentOf(state, "rep", "customer", reps), parentOf(state, "channel", "customer", channels.rows)]);
  const keys = new Set([...repOf.keys(), ...channelOf.keys()]);
  return new Map([...keys].map((key) => [key, [channelOf.get(key), repOf.get(key)].filter(Boolean).join(" · ")]));
}
