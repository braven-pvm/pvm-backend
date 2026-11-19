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
  GENERAL: "General document",
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
 * Send back to review: only a filed document whose PDF has not reached
 * Acumatica. Once attached, the file must be removed in Acumatica by hand.
 */
export function canReturnToReview(document: { state: string; acumaticaAttachedAt: string | null }): boolean {
  return document.state === "Filed" && !document.acumaticaAttachedAt;
}

/**
 * Delete document: shown only to an Admin or Operator, and only when the
 * backend says the document can go (canDelete on the detail response). The
 * workbench never repeats the rule itself.
 */
export function showDeleteAction(document: { canDelete?: boolean | null }, canWrite: boolean): boolean {
  return canWrite && document.canDelete === true;
}

export const maxDeleteReasonLength = 500;

/** The heading of what was read from a general scan (owner decision, design 6.13). */
export const readByVisionTitle = "Read by Vision";

/** A filed POD is linked to its invoice; in Shadow mode that is all the user needs to see (design 6.13). */
export function linkedToText(reference: string): string {
  return `Linked to ${reference}`;
}

/**
 * Where the PDF stands in Acumatica, for a filed document. Only in Write mode:
 * in Shadow mode nothing is attached, and "Waiting to attach" only confused
 * people, so the invoice link alone shows (design 6.13).
 */
export function acumaticaAttachLabel(
  document: { state: string; acumaticaAttachPending: boolean; acumaticaAttachedAt: string | null },
  mode: string | null | undefined,
): string | null {
  if (mode !== "Write" || document.state !== "Filed") {
    return null;
  }
  if (document.acumaticaAttachedAt) {
    return "Attached";
  }
  return document.acumaticaAttachPending ? "Waiting to attach" : "Not attached";
}

/**
 * Change invoice: shown only to an Admin or Operator, and only when the backend
 * says the invoice can change (canChangeInvoice on the detail response).
 */
export function showChangeInvoiceAction(document: { canChangeInvoice?: boolean | null }, canWrite: boolean): boolean {
  return canWrite && document.canChangeInvoice === true;
}

export const maxChangeReasonLength = 500;

/** The notice after the invoice of a document changed. */
export function invoiceChangedMessage(reference: string): string {
  return `${linkedToText(reference)}.`;
}

export function changeInvoiceErrorMessage(status: number, code: string | null | undefined, invoiceReference: string): string {
  switch (status) {
    case 409:
      if (code === "document-attached") return "The PDF is already attached to the invoice in Acumatica, so the invoice cannot be changed here.";
      if (code === "document-not-filed") return "The document is no longer filed. The page now shows its current state.";
      if (code === "not-a-pod") return "A general document has no invoice to change.";
      if (code === "document-changed") return "Someone else changed the invoice of this document. The page now shows its current invoice.";
      return "The invoice can no longer be changed. The page now shows the current state.";
    case 400:
      if (code === "invoice-not-found") return `Invoice ${invoiceReference} was not found in Acumatica. Search again and choose an invoice from the results.`;
      if (code === "same-invoice") return `The document is already linked to ${invoiceReference}.`;
      return "Choose an invoice from the search, and write a reason of at most 500 characters.";
    case 404:
      return "This document no longer exists.";
    case 403:
      return "You do not have permission to change the invoice. Ask an Admin or Operator.";
    default:
      return `The invoice could not be changed (error ${status}). Nothing changed. Try again in a minute.`;
  }
}

/** The title of the warning after a delete that left a file in storage. */
export function deleteWarningTitle(file: string | null | undefined): string {
  return file === "thumbnail" ? "The preview image is still in storage" : "The PDF is still in storage";
}

/** The notice on the Documents list after a delete. */
export const documentDeletedMessage = "Document deleted.";

export function deleteErrorMessage(status: number, code?: string | null): string {
  switch (status) {
    case 409:
      if (code === "document-filed") return "The document is filed, so it cannot be deleted. Send it back to review first.";
      if (code === "document-attached") return "The PDF is already attached to the invoice in Acumatica, so the document cannot be deleted.";
      if (code === "document-processing") return "The document is still being processed. Wait until processing ends, then try again.";
      return "The document can no longer be deleted. The page now shows its current state.";
    case 400:
      return "Write a reason of at most 500 characters that says why the document is deleted.";
    case 404:
      return "This document no longer exists. Someone may have deleted it already.";
    case 403:
      return "You do not have permission to delete documents. Ask an Admin or Operator.";
    default:
      return `The document could not be deleted (error ${status}). Nothing was deleted. Try again in a minute.`;
  }
}

/** Process again: a Failed document, or one stuck in Received or Processing. */
export function canProcessAgain(document: { state: string; stuck: boolean }): boolean {
  return document.state === "Failed" || document.stuck;
}

/**
 * What the file-to dialog promises about Acumatica. Only Write mode attaches
 * the PDF now; any other or missing mode never promises an attachment.
 */
export function fileToAttachText(mode: string | null | undefined): string {
  return mode === "Write"
    ? "The PDF will be attached to this invoice in Acumatica."
    : "The PDF will be attached in Acumatica when attachments are switched on.";
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

export function returnToReviewErrorMessage(status: number): string {
  switch (status) {
    case 409:
      return "The document is no longer filed, or its PDF is already attached in Acumatica. An attached PDF must be removed in Acumatica by hand. The page now shows the current state.";
    case 400:
      return "Write a note of at most 1000 characters that says why the document goes back to review.";
    case 404:
      return "This document no longer exists.";
    case 403:
      return "You do not have permission to send documents back to review. Ask an Admin or Operator.";
    default:
      return `The document could not be sent back to review (error ${status}). Nothing changed. Try again in a minute.`;
  }
}

export function processAgainErrorMessage(status: number): string {
  switch (status) {
    case 409:
      return "Only a failed or stuck document can be processed again. The page now shows its current state.";
    case 404:
      return "This document no longer exists.";
    case 403:
      return "You do not have permission to process documents again. Ask an Admin or Operator.";
    default:
      return `The document could not be queued (error ${status}). Nothing changed. Try again in a minute.`;
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
export function unconfirmedMessage(
  outcome: "filed" | "rejected" | "sent back to review" | "queued for processing" | "deleted" | "linked to the new invoice",
): string {
  return `We could not confirm whether the document was ${outcome}. Reload the page to see its state.`;
}

/**
 * Who uploaded the document. A phone upload names the scanner device and,
 * when the person picked one, the staff name: "Dispatch phone 1 · Thabo".
 * A workbench upload shows the uploader as the API gives it.
 */
export function uploaderText(document: {
  uploadedBy: string;
  scannerDeviceName?: string | null;
  scannerStaffName?: string | null;
}): string {
  const device = document.scannerDeviceName?.trim();
  if (!device) {
    return document.uploadedBy;
  }
  const staff = document.scannerStaffName?.trim();
  return staff ? `${device} · ${staff}` : device;
}
