import { getApiAuthHeaders } from "../auth/session";
import { timedFetch } from "./timed-fetch";

const apiBaseUrl =
  process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5000";

/** The file formats that the sell-out import reads. */
export const sellOutSources: Record<string, string> = {
  "makro-dispo": "Makro Dispo report",
  "shoprite-vas": "Shoprite group portal report",
  "wholesaler-topmed": "TopMed list",
  "wholesaler-cj": "CJ list",
  "wholesaler-alphapharm": "Alpha Pharm list",
  "wholesaler-arrienel": "Arrie Nel list",
};

export type SellOutIssue = { rowNumber: number; reason: string; rawKey?: string | null };

/** What a preview or an import of one file did, or would do. */
export type SellOutImportResult = {
  importId: string;
  /** Previewed, Loaded, Refused or Failed. */
  status: string;
  source?: string | null;
  reason?: string | null;
  months: string[];
  rowsRead: number;
  rowsLoaded: number;
  rowsUnmapped: number;
  rowsRefused: number;
  newStores: number;
  unmappedProducts: string[];
  issues: SellOutIssue[];
};

export type SellOutImportSummary = {
  id: string;
  source: string;
  fileName: string;
  fileDate: string;
  receivedBy: string;
  status: string;
  statusReason?: string | null;
  firstMonth?: string | null;
  lastMonth?: string | null;
  rowsRead: number;
  rowsLoaded: number;
  rowsUnmapped: number;
  rowsRefused: number;
  createdAt: string;
  loadedAt?: string | null;
};

export type UnmappedSellOutProduct = {
  source: string;
  productCode: string;
  gtin?: string | null;
  rows: number;
  units: number;
  stores: number;
  lastMonth: string;
};

export type SellOutProductChoice = { inventoryId: string; description: string; itemClassName?: string | null };

export async function previewSellOutFile(file: File): Promise<SellOutImportResult> {
  return postFile("/api/reports/sell-out/imports/preview", file);
}

export async function importSellOutFile(file: File): Promise<SellOutImportResult> {
  return postFile("/api/reports/sell-out/imports", file);
}

export async function getSellOutImports(): Promise<SellOutImportSummary[]> {
  return getJson("/api/reports/sell-out/imports?take=50", "the sell-out imports");
}

export async function getUnmappedSellOutProducts(): Promise<UnmappedSellOutProduct[]> {
  return getJson("/api/reports/sell-out/unmapped", "the products to map");
}

export async function getSellOutProductChoices(): Promise<SellOutProductChoice[]> {
  return getJson("/api/reports/sell-out/products", "the Acumatica items");
}

export async function mapSellOutProduct(input: {
  source: string;
  productCode: string;
  inventoryId: string;
  unitsPerEach?: number;
}): Promise<{ rowsUpdated: number }> {
  const headers = await getApiAuthHeaders();
  const response = await timedFetch(`${apiBaseUrl}/api/reports/sell-out/product-maps`, {
    method: "PUT",
    headers: { ...headers, "Content-Type": "application/json" },
    body: JSON.stringify(input),
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(await readError(response, "The product could not be mapped"));
  }

  return response.json();
}

async function getJson<T>(path: string, what: string): Promise<T> {
  const headers = await getApiAuthHeaders();
  const response = await timedFetch(`${apiBaseUrl}${path}`, { headers, cache: "no-store" });
  if (!response.ok) {
    throw new Error(`Failed to load ${what}: ${response.status}`);
  }

  return response.json();
}

async function postFile<T>(path: string, file: File): Promise<T> {
  const headers = await getApiAuthHeaders();
  const body = new FormData();
  body.append("file", file);

  // Do not set Content-Type here. fetch must set it, so that it carries the multipart boundary.
  const response = await timedFetch(`${apiBaseUrl}${path}`, {
    method: "POST",
    headers,
    body,
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(await readError(response, "The file could not be read"));
  }

  return response.json();
}

async function readError(response: Response, fallback: string): Promise<string> {
  try {
    const problem = (await response.json()) as { message?: string; detail?: string; title?: string };
    const message = problem.message ?? problem.detail ?? problem.title;
    if (message) {
      return message;
    }
  } catch {
    // The failure body was not JSON, so fall back to the status code.
  }

  return `${fallback}: ${response.status}`;
}
