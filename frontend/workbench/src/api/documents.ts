import { getApiAuthHeaders } from "../auth/session";
import { buildDocumentSearchQuery, type DocumentSearchFilters } from "../documents";
import { buildGeneralQuery, type GeneralSearchFilters } from "../general-documents";
import { timedFetch } from "./timed-fetch";

/*
 * The document endpoints (backend/src/Pvm.Api/Features/Documents/DocumentReviewEndpoints.cs).
 * Server-side only: the calls carry the user's token, which never reaches the browser.
 */

const apiBaseUrl = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5000";

export type DocumentSearchItem = {
  id: string;
  documentType: string;
  state: string;
  reviewReason: string | null;
  matchedReference: string | null;
  matchedCustomerName: string | null;
  matchedBy: string | null;
  acumaticaAttachPending: boolean;
  acumaticaAttachedAt: string | null;
  pageCount: number;
  uploadedBy: string;
  /** Set for a phone upload: the device's current name, and the staff name picked at upload. */
  scannerDeviceName?: string | null;
  scannerStaffName?: string | null;
  uploadedAt: string;
  resolvedBy: string | null;
  resolvedAt: string | null;
  rejectedAt: string | null;
  /** A general document: typed on the phone, and the title Vision read (design 6.13). */
  title?: string | null;
  autoTitle?: string | null;
  note?: string | null;
  hasThumbnail?: boolean;
};

/** The Documents search: every type unless a type is asked for, and the attach mode in force. */
export type DocumentSearchPage = DocumentPage<DocumentSearchItem> & {
  /** "Shadow" or "Write". The list shows an attach state only in Write mode. */
  acumaticaAttachMode?: string;
};

export type ReviewListItem = {
  id: string;
  documentType: string;
  state: string;
  reviewReason: string | null;
  stuck: boolean;
  pageCount: number;
  uploadedBy: string;
  /** Set for a phone upload: the device's current name, and the staff name picked at upload. */
  scannerDeviceName?: string | null;
  scannerStaffName?: string | null;
  uploadedAt: string;
  updatedAt: string;
};

export type DocumentPage<T> = {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
};

export type InvoiceCandidate = {
  referenceNumber: string;
  date: string | null;
  customerId: string | null;
  customerName: string | null;
  parentCustomerName: string | null;
  shipToName: string | null;
  status: string | null;
};

export type DocumentDetail = {
  id: string;
  clientDocumentId: string;
  documentType: string;
  state: string;
  reviewReason: string | null;
  stuck: boolean;
  pageCount: number;
  sizeBytes: number;
  matchedReference: string | null;
  matchedCustomerName: string | null;
  matchedBy: string | null;
  acumaticaAttachPending: boolean;
  acumaticaAttachedAt: string | null;
  uploadedBy: string;
  /** Set for a phone upload: the device's current name, and the staff name picked at upload. */
  scannerDeviceName?: string | null;
  scannerStaffName?: string | null;
  uploadedAt: string;
  resolvedBy: string | null;
  resolvedAt: string | null;
  reviewNote: string | null;
  rejectedBy: string | null;
  rejectedAt: string | null;
  updatedAt: string;
  extraction: Record<string, unknown> | null;
  unreadableReason: string | null;
  candidates: InvoiceCandidate[];
  /** The backend's Acumatica attach mode: "Shadow" or "Write". Read-only. */
  acumaticaAttachMode?: string;
  /** The backend's delete rule for this document as it is now (design 6.11). Missing means no. */
  canDelete?: boolean;
  /** The backend's change-invoice rule: a filed POD not attached in Acumatica (design 6.13). Missing means no. */
  canChangeInvoice?: boolean;
  /** Typed on the phone: the title (general documents only) and the note. */
  title?: string | null;
  note?: string | null;
  /** What Vision read from a general document (design 6.12). */
  autoTitle?: string | null;
  summary?: string | null;
  /** yyyy-mm-dd, printed on the paper. */
  documentDate?: string | null;
  searchWords?: string[];
  hasThumbnail?: boolean;
};

/** One row of the General scans list (GET /api/documents/general). */
export type GeneralDocumentItem = {
  id: string;
  documentType: string;
  state: string;
  title: string | null;
  note: string | null;
  autoTitle: string | null;
  summary: string | null;
  documentDate: string | null;
  searchWords: string[];
  hasThumbnail: boolean;
  pageCount: number;
  uploadedBy: string;
  uploadedAt: string;
  scannerDeviceId: string | null;
  scannerDeviceName: string | null;
  scannerStaffId: string | null;
  scannerStaffName: string | null;
};

export type GeneralFilterOption = { id: string; name: string };

/** The phones and staff names that uploaded general documents, for the list's filters. */
export type GeneralFilterOptions = { devices: GeneralFilterOption[]; staff: GeneralFilterOption[] };

export type DocumentType = { code: string; displayName: string };

/** The types the backend accepts. If the call fails, the types the workbench knows. */
export async function getDocumentTypes(): Promise<DocumentType[]> {
  try {
    const types = await getJson<DocumentType[]>("/api/documents/types", "document types");
    if (Array.isArray(types) && types.length > 0) {
      return types;
    }
  } catch (error) {
    console.warn("documents.types.unavailable", { error: String(error) });
  }
  return [
    { code: "POD", displayName: "Proof of delivery" },
    { code: "GENERAL", displayName: "General document" },
  ];
}

/**
 * ACUMATICA_BASE_URL: the Acumatica site, such as https://pvm.acumatica.com.
 * Optional and read on the server only. Without it, invoice numbers are plain
 * text; acumaticaInvoiceUrl ignores a value that is not an https URL.
 */
export function acumaticaBaseUrl(): string | undefined {
  return process.env.ACUMATICA_BASE_URL || undefined;
}

/** The result of a call whose failure the page explains to the user. */
export type ApiResult<T> = { ok: true; value: T } | { ok: false; status: number };

async function getJson<T>(path: string, what: string): Promise<T> {
  const headers = await getApiAuthHeaders();
  const response = await timedFetch(`${apiBaseUrl}${path}`, { headers, cache: "no-store" });
  if (!response.ok) {
    throw new Error(`Failed to load ${what}: ${response.status}`);
  }
  return response.json();
}

export function searchDocuments(filters: DocumentSearchFilters): Promise<DocumentSearchPage> {
  const query = buildDocumentSearchQuery(filters);
  return getJson(`/api/documents${query ? `?${query}` : ""}`, "documents");
}

export function getGeneralDocuments(filters: GeneralSearchFilters): Promise<DocumentPage<GeneralDocumentItem>> {
  const query = buildGeneralQuery(filters);
  return getJson(`/api/documents/general${query ? `?${query}` : ""}`, "the general scans");
}

/** The filter choices. If the call fails, no choices: the list still works. */
export async function getGeneralFilterOptions(): Promise<GeneralFilterOptions> {
  try {
    return await getJson<GeneralFilterOptions>("/api/documents/general/filters", "the general scan filters");
  } catch (error) {
    console.warn("documents.general.filters.unavailable", { error: String(error) });
    return { devices: [], staff: [] };
  }
}

export function getReviewList(page = 1): Promise<DocumentPage<ReviewListItem>> {
  return getJson(`/api/documents/review${page > 1 ? `?page=${page}` : ""}`, "the review list");
}

/** One document, or null when the API does not know it. */
export async function getDocument(id: string): Promise<DocumentDetail | null> {
  const headers = await getApiAuthHeaders();
  const response = await timedFetch(`${apiBaseUrl}/api/documents/${encodeURIComponent(id)}`, { headers, cache: "no-store" });
  if (response.status === 404) {
    return null;
  }
  if (!response.ok) {
    throw new Error(`Failed to load the document: ${response.status}`);
  }
  return response.json();
}

/** Invoices for the review search. 403 for a user without write access. */
export async function searchReviewInvoices(q: string): Promise<ApiResult<InvoiceCandidate[]>> {
  const headers = await getApiAuthHeaders();
  const response = await timedFetch(
    `${apiBaseUrl}/api/documents/review/invoice-search?q=${encodeURIComponent(q)}`,
    { headers, cache: "no-store" },
  );
  return response.ok ? { ok: true, value: await response.json() } : { ok: false, status: response.status };
}

async function postJson(path: string, body: unknown): Promise<ApiResult<unknown>> {
  const headers = await getApiAuthHeaders();
  const response = await timedFetch(`${apiBaseUrl}${path}`, {
    method: "POST",
    headers: { ...headers, "Content-Type": "application/json" },
    body: JSON.stringify(body),
    cache: "no-store",
  });
  if (!response.ok) {
    return { ok: false, status: response.status };
  }
  try {
    return { ok: true, value: await response.json() };
  } catch {
    return { ok: true, value: undefined };
  }
}

export function fileDocumentTo(id: string, invoiceReference: string): Promise<ApiResult<unknown>> {
  return postJson(`/api/documents/${encodeURIComponent(id)}/file-to`, { invoiceReference });
}

export function rejectDocument(id: string, note: string): Promise<ApiResult<unknown>> {
  return postJson(`/api/documents/${encodeURIComponent(id)}/reject`, { note });
}

export function returnDocumentToReview(id: string, note: string): Promise<ApiResult<unknown>> {
  return postJson(`/api/documents/${encodeURIComponent(id)}/return-to-review`, { note });
}

export function processDocumentAgain(id: string): Promise<ApiResult<unknown>> {
  return postJson(`/api/documents/${encodeURIComponent(id)}/process-again`, {});
}

export type DeleteDocumentResult =
  /** warningFile: "pdf" or "thumbnail", the file that stayed in storage. */
  | { ok: true; warning: string | null; warningFile: string | null }
  | { ok: false; status: number; code: string | null };

export type ChangeInvoiceResult = { ok: true } | { ok: false; status: number; code: string | null };

/** Links a filed POD to another invoice; nothing is written to Acumatica. Refusals carry a stable `code`. */
export async function changeDocumentInvoice(id: string, invoiceReference: string, reason: string | null): Promise<ChangeInvoiceResult> {
  const headers = await getApiAuthHeaders();
  const response = await timedFetch(`${apiBaseUrl}/api/documents/${encodeURIComponent(id)}/change-invoice`, {
    method: "POST",
    headers: { ...headers, "Content-Type": "application/json" },
    body: JSON.stringify({ invoiceReference, reason }),
    cache: "no-store",
  });
  if (response.ok) {
    return { ok: true };
  }
  let body: { code?: unknown } | null = null;
  try {
    body = await response.json();
  } catch {
    body = null;
  }
  return { ok: false, status: response.status, code: typeof body?.code === "string" ? body.code : null };
}

/** Deletes an unfiled document and its PDF for good. A 409 carries a stable `code`. */
export async function deleteDocument(id: string, reason: string): Promise<DeleteDocumentResult> {
  const headers = await getApiAuthHeaders();
  const response = await timedFetch(`${apiBaseUrl}/api/documents/${encodeURIComponent(id)}/delete`, {
    method: "POST",
    headers: { ...headers, "Content-Type": "application/json" },
    body: JSON.stringify({ reason }),
    cache: "no-store",
  });
  let body: { code?: unknown; warning?: unknown; warningFile?: unknown } | null = null;
  try {
    body = await response.json();
  } catch {
    body = null;
  }
  if (!response.ok) {
    return { ok: false, status: response.status, code: typeof body?.code === "string" ? body.code : null };
  }
  return {
    ok: true,
    warning: typeof body?.warning === "string" ? body.warning : null,
    warningFile: typeof body?.warningFile === "string" ? body.warningFile : null,
  };
}

/** The first-page JPEG, as the backend streams it. For the workbench's own thumbnail route only. */
export async function fetchDocumentThumbnail(id: string): Promise<Response | null> {
  const headers = await getApiAuthHeaders();
  if (Object.keys(headers).length === 0) {
    return null;
  }
  return timedFetch(`${apiBaseUrl}/api/documents/${encodeURIComponent(id)}/thumbnail`, { headers, cache: "no-store" });
}

/** The PDF, as the backend streams it. For the workbench's own file route only. */
export async function fetchDocumentFile(id: string): Promise<Response | null> {
  const headers = await getApiAuthHeaders();
  if (Object.keys(headers).length === 0) {
    return null;
  }
  return timedFetch(`${apiBaseUrl}/api/documents/${encodeURIComponent(id)}/file`, { headers, cache: "no-store" });
}
