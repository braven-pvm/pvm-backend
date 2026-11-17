/*
 * Pure helpers for the Documents pages: labels, the search query and the
 * messages for the review actions. The API shapes are in src/api/documents.ts.
 * Design: docs/superpowers/specs/2026-11-17-document-capture-design.md 6.7, 6.8.
 */

export type DocumentState = "Received" | "Processing" | "Filed" | "NeedsReview" | "Failed";

export type ReviewReason =
  | "NoCandidate"
  | "MultipleCandidates"
  | "NoSecondSignal"
  | "ClientNameConflict"
  | "ReadFailure";

const stateLabels: Record<string, string> = {
  Received: "Received",
  Processing: "Processing",
  Filed: "Filed",
  NeedsReview: "Needs review",
  Failed: "Failed",
};

export const stuckLabel = "Stuck — processing did not finish";

/** The state as staff read it. A stuck document is named stuck, whatever its state. */
export function formatDocumentState(state: string, stuck = false): string {
  if (stuck) {
    return stuckLabel;
  }
  return stateLabels[state] ?? state;
}

/** The states a person can search by, in the order they happen. */
export const documentStates: { value: DocumentState; label: string }[] = [
  { value: "Received", label: "Received" },
  { value: "Processing", label: "Processing" },
  { value: "Filed", label: "Filed" },
  { value: "NeedsReview", label: "Needs review" },
  { value: "Failed", label: "Failed" },
];

const reasonLabels: Record<ReviewReason, string> = {
  NoCandidate: "No matching invoice found",
  MultipleCandidates: "More than one possible invoice",
  NoSecondSignal: "Only one number matched — needs a check",
  ClientNameConflict: "Client name does not match the invoice",
  ReadFailure: "The document could not be read",
};

/** Why the document needs a person. Only NeedsReview and Failed documents show a reason. */
export function formatReviewReason(reason: string | null | undefined, state: string): string | null {
  if (!reason || (state !== "NeedsReview" && state !== "Failed")) {
    return null;
  }
  return reasonLabels[reason as ReviewReason] ?? reason;
}

const typeLabels: Record<string, string> = {
  POD: "Proof of delivery",
};

export function documentTypeLabel(code: string): string {
  return typeLabels[code.toUpperCase()] ?? code;
}

export type DocumentSearchFilters = {
  invoice?: string;
  client?: string;
  type?: string;
  state?: string;
  /** yyyy-mm-dd, the first upload day to include. */
  from?: string;
  /** yyyy-mm-dd, the last upload day to include. */
  to?: string;
  page?: number | string;
};

const isoDay = /^(\d{4})-(\d{2})-(\d{2})$/;

function validDay(value: string | undefined): string | null {
  const trimmed = value?.trim() ?? "";
  const match = isoDay.exec(trimmed);
  if (!match) {
    return null;
  }
  const date = new Date(`${trimmed}T00:00:00Z`);
  // Rejects days such as 2026-02-31, which Date rolls over into March.
  return !Number.isNaN(date.getTime()) && date.toISOString().startsWith(trimmed) ? trimmed : null;
}

function nextDay(day: string): string {
  const date = new Date(`${day}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + 1);
  return date.toISOString().slice(0, 10);
}

/**
 * The query string for a document search. Empty fields are left out and page 1
 * is not sent. For the API (the default), dates become South African day
 * bounds: "from" the start of that day, "to" the start of the day after the
 * end date, because the API's "to" is exclusive. For a browser URL
 * (forApi: false) the dates stay as typed, so the form shows them again.
 */
export function buildDocumentSearchQuery(filters: DocumentSearchFilters, options: { forApi?: boolean } = {}): string {
  const forApi = options.forApi ?? true;
  const query = new URLSearchParams();
  for (const key of ["invoice", "client", "type", "state"] as const) {
    const value = filters[key]?.trim();
    if (value) {
      query.set(key, value);
    }
  }

  const from = validDay(filters.from);
  if (from) {
    query.set("from", forApi ? `${from}T00:00:00+02:00` : from);
  }
  const to = validDay(filters.to);
  if (to) {
    query.set("to", forApi ? `${nextDay(to)}T00:00:00+02:00` : to);
  }

  const page = Number(filters.page);
  if (Number.isInteger(page) && page > 1) {
    query.set("page", String(page));
  }

  return query.toString();
}

/** File-to and reject apply only to a document on the review list that did not get stuck. */
export function canActOnDocument(document: { state: string; stuck: boolean }): boolean {
  return !document.stuck && (document.state === "NeedsReview" || document.state === "Failed");
}

/**
 * One field of the extracted data. The extractor's keys are not guaranteed
 * to be camel case, so the key is matched without regard to case.
 */
export function readExtraction(extraction: Record<string, unknown> | null | undefined, key: string): unknown {
  if (!extraction) {
    return undefined;
  }
  const wanted = key.toLowerCase();
  const found = Object.keys(extraction).find((name) => name.toLowerCase() === wanted);
  return found === undefined ? undefined : extraction[found];
}

const resolvedElsewhere = "Someone else already resolved this document. The page now shows its current state.";

export function fileToErrorMessage(status: number, invoiceReference: string): string {
  switch (status) {
    case 409:
      return resolvedElsewhere;
    case 400:
      return `Invoice ${invoiceReference} was not found in Acumatica. Search again and choose an invoice from the results.`;
    case 404:
      return "This document no longer exists.";
    case 403:
      return "You do not have permission to file documents. Ask an Admin or Operator.";
    default:
      return `The document could not be filed (error ${status}). Nothing was filed. Try again in a minute.`;
  }
}

export function rejectErrorMessage(status: number): string {
  switch (status) {
    case 409:
      return resolvedElsewhere;
    case 400:
      return "Write a note of at most 1000 characters that says why the document is rejected.";
    case 404:
      return "This document no longer exists.";
    case 403:
      return "You do not have permission to reject documents. Ask an Admin or Operator.";
    default:
      return `The document could not be rejected (error ${status}). Nothing changed. Try again in a minute.`;
  }
}

/** A document id is a GUID. Checked before an id goes into an API path. */
export const documentIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isDocumentId(value: unknown): value is string {
  return typeof value === "string" && documentIdPattern.test(value);
}

/**
 * The Acumatica invoice screen (AR301000) for a reference, or null. The base
 * URL comes from the ACUMATICA_BASE_URL setting; anything that is not an
 * https URL is ignored, so a bad setting gives plain text, never a bad link.
 */
export function acumaticaInvoiceUrl(baseUrl: string | null | undefined, reference: string | null | undefined): string | null {
  const ref = reference?.trim();
  if (!baseUrl || !ref) {
    return null;
  }
  let url: URL;
  try {
    url = new URL(baseUrl.trim());
  } catch {
    return null;
  }
  if (url.protocol !== "https:") {
    return null;
  }
  const base = `${url.origin}${url.pathname}`.replace(/\/+$/, "");
  return `${base}/Main?ScreenId=AR301000&DocType=INV&RefNbr=${encodeURIComponent(ref)}`;
}

/** When the API call itself failed (timeout, network), the write may or may not have happened. */
export function unconfirmedMessage(outcome: "filed" | "rejected"): string {
  return `We could not confirm whether the document was ${outcome}. Reload the page to see its state.`;
}
