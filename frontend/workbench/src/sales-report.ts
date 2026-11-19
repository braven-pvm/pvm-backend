/*
 * Helpers of the sales report page. Refer to docs/design/sales-report-pages.md, section 4.1.
 * They have no React and no fetch, so the tests run in plain Node.
 *
 * The URL holds the whole state of the page, so that a person can share it, reload it
 * and save it as a named filter.
 */

export const dimensions = [
  { id: "rep", label: "Rep", plural: "Reps" },
  { id: "channel", label: "Channel", plural: "Channels" },
  { id: "customerGroup", label: "Customer group", plural: "Customer groups" },
  { id: "customer", label: "Customer", plural: "Customers" },
  { id: "province", label: "Province", plural: "Provinces" },
  { id: "itemGroup", label: "Item group", plural: "Item groups" },
  { id: "product", label: "Product", plural: "Products" },
  { id: "warehouse", label: "Warehouse", plural: "Warehouses" },
  { id: "repAtSale", label: "Rep at the sale", plural: "Reps at the sale" },
] as const;

export type Dimension = (typeof dimensions)[number]["id"];
export type Measure = "amount" | "quantity" | "customers";
export type ColumnMode = "months" | "years" | "none";

export type ReportState = {
  rows: Dimension[];
  /** Calendar months, "2026-03". */
  from: string;
  to: string;
  measure: Measure;
  columns: ColumnMode;
  compare: boolean;
  sort: "value" | "label";
  open: string[];
  include: Partial<Record<Dimension, string[]>>;
};

export const defaultRows: Dimension[] = ["rep", "customer", "product"];
export const maxRows = 5;
export const maxMonths = 36;

const dimensionIds = new Set<string>(dimensions.map((dimension) => dimension.id));

export function isDimension(value: string): value is Dimension {
  return dimensionIds.has(value);
}

export function dimensionLabel(id: Dimension): string {
  return dimensions.find((dimension) => dimension.id === id)?.label ?? id;
}

/** The financial year of a date. The year runs from March to February: March 2026 is in 2027. */
export function financialYearOf(date: Date): number {
  return date.getUTCMonth() >= 2 ? date.getUTCFullYear() + 1 : date.getUTCFullYear();
}

/** "2026-03" for a date. */
export function monthOf(date: Date): string {
  return `${date.getUTCFullYear()}-${String(date.getUTCMonth() + 1).padStart(2, "0")}`;
}

/** Adds months to "2026-03". */
export function addMonths(month: string, count: number): string {
  const [year, number] = month.split("-").map(Number);
  const index = year * 12 + (number - 1) + count;
  return `${Math.floor(index / 12)}-${String((index % 12) + 1).padStart(2, "0")}`;
}

/** The number of months from "2026-03" to "2026-05": 3. */
export function monthCount(from: string, to: string): number {
  const [fromYear, fromMonth] = from.split("-").map(Number);
  const [toYear, toMonth] = to.split("-").map(Number);
  return toYear * 12 + toMonth - (fromYear * 12 + fromMonth) + 1;
}

/** "Mar 2026" for "2026-03". */
export function monthLabel(month: string): string {
  const [year, number] = month.split("-").map(Number);
  return `${["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"][number - 1]} ${year}`;
}

/** The first month of the financial year of a date. */
export function financialYearStart(date: Date): string {
  return `${financialYearOf(date) - 1}-03`;
}

/** The choices of the period. "custom" keeps the months in the URL. */
export function periodPresets(today = new Date()) {
  const thisMonth = monthOf(today);
  const start = financialYearStart(today);
  const year = financialYearOf(today);
  return [
    { id: "fytd", label: `FY${year} to date`, from: start, to: thisMonth },
    { id: "lastfy", label: `FY${year - 1}`, from: addMonths(start, -12), to: addMonths(start, -1) },
    { id: "last12", label: "Last 12 months", from: addMonths(thisMonth, -11), to: thisMonth },
    { id: "last3", label: "Last 3 months", from: addMonths(thisMonth, -2), to: thisMonth },
    { id: "lastmonth", label: "Last month", from: addMonths(thisMonth, -1), to: addMonths(thisMonth, -1) },
    { id: "thismonth", label: "This month", from: thisMonth, to: thisMonth },
  ];
}

function list(value: string | string[] | undefined): string[] {
  const values = Array.isArray(value) ? value : value ? [value] : [];
  return values
    .flatMap((item) => item.split(","))
    .map((item) => item.trim())
    .filter((item) => item.length > 0);
}

const monthPattern = /^\d{4}-(0[1-9]|1[0-2])$/;

/** Reads the state from the search parameters of the page. A wrong value falls back to the default. */
export function readState(search: Record<string, string | string[] | undefined>, today = new Date()): ReportState {
  const rows: Dimension[] = [];
  for (const id of list(search.rows)) {
    if (isDimension(id) && !rows.includes(id) && rows.length < maxRows) {
      rows.push(id);
    }
  }

  let from = list(search.from)[0] ?? "";
  let to = list(search.to)[0] ?? "";
  if (!monthPattern.test(from) || !monthPattern.test(to) || monthCount(from, to) < 1 || monthCount(from, to) > maxMonths) {
    from = financialYearStart(today);
    to = monthOf(today);
  }

  const include: ReportState["include"] = {};
  for (const dimension of dimensions) {
    const keys = [...new Set(list(search[dimension.id]))];
    if (keys.length > 0) {
      include[dimension.id] = keys;
    }
  }

  const measure = list(search.measure)[0];
  const columns = list(search.columns)[0];
  return {
    rows: rows.length > 0 ? rows : [...defaultRows],
    from,
    to,
    measure: measure === "quantity" || measure === "customers" ? measure : "amount",
    columns: columns === "years" || columns === "none" ? columns : "months",
    compare: list(search.compare)[0] !== "0",
    sort: list(search.sort)[0] === "label" ? "label" : "value",
    open: [...new Set(list(search.open))],
    include,
  };
}

/**
 * The query string of a state, in a fixed order, for links and for saved filters.
 * Defaults stay out, so that the query is short. "open" stays out when withOpen is false.
 */
export function stateQuery(state: ReportState, withOpen = true): URLSearchParams {
  const query = new URLSearchParams();
  query.set("rows", state.rows.join(","));
  query.set("from", state.from);
  query.set("to", state.to);
  for (const dimension of dimensions) {
    const keys = state.include[dimension.id];
    if (keys && keys.length > 0) {
      query.set(dimension.id, keys.join(","));
    }
  }
  if (state.measure !== "amount") {
    query.set("measure", state.measure);
  }
  if (state.columns !== "months") {
    query.set("columns", state.columns);
  }
  if (!state.compare) {
    query.set("compare", "0");
  }
  if (state.sort !== "value") {
    query.set("sort", state.sort);
  }
  if (withOpen && state.open.length > 0) {
    query.set("open", state.open.join(","));
  }
  return query;
}

/** The query string of the API for a state. */
export function apiQuery(state: ReportState): URLSearchParams {
  const query = stateQuery(state);
  query.set("compare", state.compare ? "1" : "0");
  return query;
}

/** A link to the page with a changed state. */
export function hrefFor(path: string, state: ReportState): string {
  const query = stateQuery(state).toString();
  return query ? `${path}?${query}` : path;
}

/** Opens a row, or closes it and every row under it. */
export function toggleOpen(open: string[], key: string): string[] {
  if (!open.includes(key)) {
    return [...open, key];
  }
  return open.filter((item) => item !== key && !item.startsWith(`${key}|`));
}

/**
 * Shows only one row: each dimension on the path of the row keeps only the key of the row,
 * and those dimensions leave the rows, so the next dimensions show under the selection.
 * When no dimension is left, the last one stays.
 */
export function focusRow(state: ReportState, key: string, level: number): ReportState {
  const keys = key.split("|");
  const include = { ...state.include };
  const path = state.rows.slice(0, level + 1);
  path.forEach((dimension, index) => {
    include[dimension] = [keys[index] ?? ""];
  });
  const rows = state.rows.slice(level + 1);
  return { ...state, include, rows: rows.length > 0 ? rows : [state.rows[level]], open: [] };
}

/** A figure in a report grid: whole rands or units, a space between thousands. */
export function formatFigure(value: number): string {
  const rounded = Math.sign(value) * Math.round(Math.abs(value));
  if (rounded === 0) {
    return "0";
  }
  const sign = rounded < 0 ? "-" : "";
  return sign + Math.abs(rounded).toString().replace(/\B(?=(\d{3})+(?!\d))/g, " ");
}

/** The change against the year before: "+12.5%", or "New" with no sales a year before. */
export function formatChange(current: number, previous: number): { text: string; tone: "up" | "down" | "flat" | "new" } {
  if (previous === 0) {
    return current === 0 ? { text: "–", tone: "flat" } : { text: "New", tone: "new" };
  }
  const percent = ((current - previous) / Math.abs(previous)) * 100;
  if (Math.abs(percent) < 0.05) {
    return { text: "0.0%", tone: "flat" };
  }
  return { text: `${percent > 0 ? "+" : ""}${percent.toFixed(1)}%`, tone: percent > 0 ? "up" : "down" };
}

/** "R 1.2 m", "R 850 k": short amounts for a chart axis. */
export function shortFigure(value: number, measure: Measure): string {
  const prefix = measure === "amount" ? "R " : "";
  if (Math.abs(value) >= 1_000_000) {
    return `${prefix}${(value / 1_000_000).toFixed(1)} m`;
  }
  if (Math.abs(value) >= 1_000) {
    return `${prefix}${Math.round(value / 1_000)} k`;
  }
  return `${prefix}${formatFigure(value)}`;
}

/** The number of selected values, for the "Clear filters" link. */
export function selectionCount(state: ReportState): number {
  return Object.values(state.include).reduce((sum, keys) => sum + (keys?.length ?? 0), 0);
}

/*
 * The parts of the prototype pages: the status of a customer, the periods of the filter
 * bar, and the trail of a drill-down.
 */

export type CustomerStatus = "Growing" | "Steady" | "Declining" | "New" | "At risk" | "Lost";

export const customerStatuses: CustomerStatus[] = ["Growing", "Steady", "Declining", "New", "At risk", "Lost"];

/**
 * The status of a customer, as the prototype defines it. monthsSinceOrder is the number of
 * months since the last month with sales (0: this month), or null with no sales in the window.
 * - Lost: no order in the last 4 months, or none in the window, but sales a year before.
 * - New: no sales a year before.
 * - At risk: no order in the last 2 months.
 * - Growing: up more than 10%. Declining: down more than 10%. Else Steady.
 */
export function statusOf(current: number, previous: number, monthsSinceOrder: number | null): CustomerStatus {
  if (monthsSinceOrder === null || monthsSinceOrder >= 4) {
    return previous > 0 || monthsSinceOrder !== null ? "Lost" : "New";
  }
  if (previous <= 0 && current > 0) {
    return "New";
  }
  if (monthsSinceOrder >= 2) {
    return "At risk";
  }
  if (previous > 0 && current / previous > 1.1) {
    return "Growing";
  }
  if (previous > 0 && current / previous < 0.9) {
    return "Declining";
  }
  return "Steady";
}

/** The months since the last month with sales: 0 for the last value. Null with no sales. */
export function monthsSinceLast(values: number[]): number | null {
  for (let index = values.length - 1; index >= 0; index -= 1) {
    if (values[index] > 0) {
      return values.length - 1 - index;
    }
  }
  return null;
}

/** The periods of the prototype filter bar, then "custom" in the page. */
export const prototypePeriods = ["fytd", "lastmonth", "last3", "last12"] as const;

/** "Mar to Nov 2026, against Mar to Nov 2025". */
export function periodNote(from: string, to: string): string {
  const range = (a: string, b: string) => (a === b ? monthLabel(a) : `${monthLabel(a).slice(0, 3)}${a.slice(0, 4) === b.slice(0, 4) ? "" : ` ${a.slice(0, 4)}`} to ${monthLabel(b)}`);
  return `${range(from, to)}, against ${range(addMonths(from, -12), addMonths(to, -12))}`;
}

/** One step of a drill-down: a dimension and the key that the person opened. */
export type TrailStep = { dimension: Dimension; key: string };

/** Reads the trail "rep:R01|product:P05". Wrong steps drop out. */
export function readTrail(value: string | string[] | undefined): TrailStep[] {
  const text = Array.isArray(value) ? value[0] : value;
  return (text ?? "")
    .split("|")
    .map((step) => {
      const at = step.indexOf(":");
      return { dimension: step.slice(0, at), key: step.slice(at + 1) };
    })
    .filter((step): step is TrailStep => isDimension(step.dimension) && step.key.length > 0)
    .slice(0, maxRows);
}

export function trailText(trail: TrailStep[]): string {
  return trail.map((step) => `${step.dimension}:${step.key}`).join("|");
}

/**
 * The state of a page in a drill-down: the filters, and each step of the trail as a selection
 * of one key. So a product page under a rep shows the sales of that product by that rep.
 */
export function withTrail(state: ReportState, trail: TrailStep[]): ReportState {
  const include = { ...state.include };
  for (const step of trail) {
    include[step.dimension] = [step.key];
  }
  return { ...state, include };
}
