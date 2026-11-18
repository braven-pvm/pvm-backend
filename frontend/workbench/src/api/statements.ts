import { getApiAuthHeaders } from "../auth/session";
import { timedFetch } from "./timed-fetch";

const apiBaseUrl =
  process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5000";

export type StatementCompanySummary = { id: string; name: string; validFrom: string; validTo?: string | null };

export type StatementCustomer = { customerId: string; name: string; amount: number };
export type StatementLine = { label: string; amount: number; customers: StatementCustomer[] };
export type StatementArea = {
  name: string;
  lines: StatementLine[];
  makroValue: number;
  makroStores: { name: string; value: number }[];
  total: number;
  commissionRatePercent?: number | null;
  commission?: number | null;
};
export type DisChemYearOnYear = {
  months: string[];
  stores: { name: string; thisYear: number[]; lastYear: number[]; growthPercent?: number | null }[];
};
export type MerchandiserStatement = {
  company: string;
  title: string;
  firstMonth: string;
  lastMonth: string;
  areas: StatementArea[];
  grandTotal: number;
  ratePercent?: number | null;
  commission?: number | null;
  retainerAmount?: number | null;
  makroIsPartial: boolean;
  disChem?: DisChemYearOnYear | null;
};

export async function getStatementCompanies(): Promise<StatementCompanySummary[]> {
  const headers = await getApiAuthHeaders();
  const response = await timedFetch(`${apiBaseUrl}/api/reports/statements/companies`, { headers, cache: "no-store" });
  if (!response.ok) {
    throw new Error(`Failed to load the merchandiser companies: ${response.status}`);
  }
  return response.json();
}

/** The statement, or a message when there is no statement for the choice. */
export async function getStatement(company: string, from: string, to?: string): Promise<{ statement?: MerchandiserStatement; message?: string }> {
  const headers = await getApiAuthHeaders();
  const query = new URLSearchParams({ company, from, ...(to ? { to } : {}) });
  const response = await timedFetch(`${apiBaseUrl}/api/reports/statements?${query}`, { headers, cache: "no-store" });
  if (response.status === 400 || response.status === 404) {
    const problem = (await response.json().catch(() => ({}))) as { message?: string };
    return { message: problem.message ?? "There is no statement for this choice." };
  }
  if (!response.ok) {
    throw new Error(`Failed to load the statement: ${response.status}`);
  }
  return { statement: await response.json() };
}
