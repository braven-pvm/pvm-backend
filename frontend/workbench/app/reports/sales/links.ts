import { hrefFor, type ReportState } from "../../../src/sales-report";

/** A link to a page of the report with a state. It keeps the saved filter that the page shows. */
export function reportHref(path: string, state: ReportState, saved?: string) {
  const href = hrefFor(path, state);
  return saved ? `${href}${href.includes("?") ? "&" : "?"}saved=${encodeURIComponent(saved)}` : href;
}
