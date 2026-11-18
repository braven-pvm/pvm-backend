import { getApiAuthHeaders } from "../auth/session";
import type { SalesReportColumn } from "../sales-report";
import { timedFetch } from "./timed-fetch";

const apiBaseUrl = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5000";

export type SalesReportView = "by-rep" | "by-product" | "customers";

export type SalesReportRow = { level: number; key: string; label: string; values: number[]; total: number; hasChildren: boolean };

export type SalesReportGrid = {
  levelNames: string[];
  columns: SalesReportColumn[];
  rows: SalesReportRow[];
  totals: { values: number[]; total: number };
  dataTo?: string | null;
};

export type SalesReportOverview = {
  financialYear: number;
  months: { month: number; label: string; thisYear: number; lastYear: number }[];
  thisYearTotal: number;
  lastYearTotal: number;
  activeCustomers: number;
  dataTo?: string | null;
};

export type SalesReportLine = {
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

export type SalesReportLinePage = { totalLines: number; page: number; pageSize: number; lines: SalesReportLine[] };

export type SalesReportOption = { value: string; label: string };
export type SalesReportOptions = { reps: SalesReportOption[]; provinces: string[]; itemGroups: SalesReportOption[]; channels: string[] };

/** The answer of the API, or the message of a refused filter. */
export type Answer<T> = { data?: T; message?: string };

async function read<T>(path: string, query: URLSearchParams, what: string): Promise<Answer<T>> {
  const headers = await getApiAuthHeaders();
  const response = await timedFetch(`${apiBaseUrl}/api/reports/sales/${path}?${query}`, { headers, cache: "no-store" });
  if (response.status === 400 || response.status === 404) {
    const problem = (await response.json().catch(() => ({}))) as { message?: string };
    return { message: problem.message ?? "The filters are not correct." };
  }
  if (!response.ok) {
    throw new Error(`Failed to load ${what}: ${response.status}`);
  }
  return { data: (await response.json()) as T };
}

export function getSalesReportGrid(view: SalesReportView, query: URLSearchParams, open: string[]) {
  const withOpen = new URLSearchParams(query);
  if (open.length > 0) {
    withOpen.set("open", open.join(","));
  }
  return read<SalesReportGrid>(view, withOpen, "the sales report");
}

export function getSalesReportOverview(query: URLSearchParams) {
  return read<SalesReportOverview>("overview", query, "the sales overview");
}

export function getSalesReportLines(query: URLSearchParams, page: number) {
  const withPage = new URLSearchParams(query);
  withPage.set("page", String(page));
  return read<SalesReportLinePage>("data", withPage, "the sales lines");
}

export async function getSalesReportOptions(): Promise<SalesReportOptions> {
  const answer = await read<SalesReportOptions>("options", new URLSearchParams(), "the report filters");
  return answer.data ?? { reps: [], provinces: [], itemGroups: [], channels: [] };
}

/** The CSV file of a view, with the user's sign-in. */
export async function fetchSalesReportCsv(view: SalesReportView, query: URLSearchParams) {
  const withFormat = new URLSearchParams(query);
  withFormat.set("format", "csv");
  return timedFetch(`${apiBaseUrl}/api/reports/sales/${view}?${withFormat}`, { headers: await getApiAuthHeaders(), cache: "no-store" });
}
