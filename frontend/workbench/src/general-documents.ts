/*
 * Pure helpers for the General scans pages (design 6.12): the title shown for
 * a scan, the one-line note, and the list query. The API shapes are in
 * src/api/documents.ts; the node tests in general-documents.test.mjs cover them.
 */
// No imports: node --test loads this file on its own (see documents.ts for the same day rules).

export const generalDocumentType = "GENERAL";

export const untitledScan = "Untitled scan";

export function isGeneralDocument(documentType: string | null | undefined): boolean {
  return documentType?.toUpperCase() === generalDocumentType;
}

/** The title typed on the phone, else the one Claude read, else "Untitled scan". */
export function generalTitle(document: { title?: string | null; autoTitle?: string | null }): string {
  return document.title?.trim() || document.autoTitle?.trim() || untitledScan;
}

/** The note on one line, cut to `max` characters with an ellipsis. Null when there is none. */
export function noteLine(note: string | null | undefined, max = 80): string | null {
  const line = note?.split(/\s+/).filter(Boolean).join(" ") ?? "";
  if (!line) {
    return null;
  }
  return line.length <= max ? line : `${line.slice(0, max - 1).trimEnd()}…`;
}

/** "Dispatch phone 1 · Thabo", or the uploader for a workbench upload. */
export function phoneAndStaff(document: {
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

const isoDay = /^\d{4}-\d{2}-\d{2}$/;

/** A real yyyy-mm-dd day, or null; 2026-02-31 is not one. */
function validDay(value: string | undefined): string | null {
  const trimmed = value?.trim() ?? "";
  if (!isoDay.test(trimmed)) {
    return null;
  }
  const date = new Date(`${trimmed}T00:00:00Z`);
  return !Number.isNaN(date.getTime()) && date.toISOString().startsWith(trimmed) ? trimmed : null;
}

function nextDay(day: string): string {
  const date = new Date(`${day}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + 1);
  return date.toISOString().slice(0, 10);
}

export type GeneralSearchFilters = {
  q?: string;
  /** A scanner device ID. */
  device?: string;
  /** A staff name ID. */
  staff?: string;
  /** yyyy-mm-dd, the first upload day to include. */
  from?: string;
  /** yyyy-mm-dd, the last upload day to include. */
  to?: string;
  page?: number | string;
};

/**
 * The query string for the General scans list. Empty fields and page 1 are
 * left out. For the API (the default) the days become South African day
 * bounds, "to" exclusive; for a browser URL (forApi: false) they stay as typed.
 */
export function buildGeneralQuery(filters: GeneralSearchFilters, options: { forApi?: boolean } = {}): string {
  const forApi = options.forApi ?? true;
  const query = new URLSearchParams();
  for (const key of ["q", "device", "staff"] as const) {
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
