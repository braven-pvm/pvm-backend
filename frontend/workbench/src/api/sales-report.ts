import { getApiAuthHeaders } from "../auth/session";
import type { Dimension } from "../sales-report";
import { timedFetch } from "./timed-fetch";

const apiBaseUrl = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5000";

export type PivotRow = {
  level: number;
  key: string;
  label: string;
  values: number[];
  total: number;
  previous?: number[] | null;
  previousTotal?: number | null;
  hasChildren: boolean;
};

export type Pivot = {
  dimensions: string[];
  columns: { key: number; label: string }[];
  rows: PivotRow[];
  totals: { values: number[]; total: number; previous?: number[] | null; previousTotal?: number | null };
  dataTo?: string | null;
};

export type DimensionValue = { key: string; label: string; total: number };

export type SalesMeta = { firstMonth?: number | null; lastMonth?: number | null; dataTo?: string | null };

export type SalesLine = {
  documentType: string;
  referenceNumber: string;
  documentDate: string;
  financialYear: number;
  financialMonth: number;
  customerId: string;
  customerName?: string | null;
  customerGroup?: string | null;
  rep?: string | null;
  inventoryId: string;
  product?: string | null;
  itemGroup?: string | null;
  quantity: number;
  amount: number;
};

export type SalesLinePage = { totalLines: number; page: number; pageSize: number; lines: SalesLine[] };

export type SavedFilter = { id: string; name: string; query: string; ownerName: string; shared: boolean; mine: boolean; updatedAt: string };

/** The answer of the API, or the message of a refused request. */
export type Answer<T> = { data?: T; message?: string };

const base = `${apiBaseUrl}/api/reports/sales`;

async function send<T>(path: string, init: RequestInit, what: string): Promise<Answer<T>> {
  const headers = { ...(await getApiAuthHeaders()), ...(init.body ? { "Content-Type": "application/json" } : {}) };
  const response = await timedFetch(`${base}/${path}`, { ...init, headers, cache: "no-store" });
  if ([400, 403, 404, 409].includes(response.status)) {
    const problem = (await response.json().catch(() => ({}))) as { message?: string };
    return { message: problem.message ?? `The request for ${what} was refused.` };
  }
  if (!response.ok) {
    throw new Error(`Failed to load ${what}: ${response.status}`);
  }
  if (response.status === 204) {
    return { data: undefined };
  }
  return { data: (await response.json()) as T };
}

export function getPivot(query: URLSearchParams) {
  return send<Pivot>(`pivot?${query}`, {}, "the sales report");
}

/** The channels of the channel table, then "Other". It reads no sales lines. */
export async function getChannels(): Promise<string[]> {
  return (await send<string[]>("channels", {}, "the channels")).data ?? [];
}

export function getMeta() {
  return send<SalesMeta>("meta", {}, "the sales months");
}

/** The values of one dimension in the filter, the largest first. */
export function getValues(dimension: Dimension, query: URLSearchParams, search: string) {
  const withDimension = new URLSearchParams(query);
  withDimension.delete("open");
  withDimension.delete("rows");
  withDimension.set("dimension", dimension);
  withDimension.set("limit", "100");
  if (search.trim()) {
    withDimension.set("search", search.trim());
  }
  return send<DimensionValue[]>(`values?${withDimension}`, {}, "the filter values");
}

export function getLabels(dimension: Dimension, keys: string[]) {
  const query = new URLSearchParams({ dimension, keys: keys.join(",") });
  return send<DimensionValue[]>(`labels?${query}`, {}, "the filter labels");
}

export function getLines(query: URLSearchParams, page: number) {
  const withPage = new URLSearchParams(query);
  withPage.set("page", String(page));
  return send<SalesLinePage>(`lines?${withPage}`, {}, "the sales lines");
}

export function listSavedFilters() {
  return send<SavedFilter[]>("saved-filters", {}, "the saved filters");
}

export function createSavedFilter(body: { name: string; query: string; shared: boolean }) {
  return send<SavedFilter>("saved-filters", { method: "POST", body: JSON.stringify(body) }, "the saved filter");
}

export function updateSavedFilter(id: string, body: { name?: string; query?: string; shared?: boolean }) {
  return send<SavedFilter>(`saved-filters/${encodeURIComponent(id)}`, { method: "PUT", body: JSON.stringify(body) }, "the saved filter");
}

export function deleteSavedFilter(id: string) {
  return send<void>(`saved-filters/${encodeURIComponent(id)}`, { method: "DELETE" }, "the saved filter");
}

/** The CSV file of the pivot, with the user's sign-in. */
export async function fetchPivotCsv(query: URLSearchParams) {
  const withFormat = new URLSearchParams(query);
  withFormat.set("format", "csv");
  return timedFetch(`${base}/pivot?${withFormat}`, { headers: await getApiAuthHeaders(), cache: "no-store" });
}
