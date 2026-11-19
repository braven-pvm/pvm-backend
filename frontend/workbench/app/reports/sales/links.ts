import { hrefFor, stateQuery, trailText, type ReportState, type TrailStep } from "../../../src/sales-report";

/** A link to a page of the report with a state. It keeps the saved filter that the page shows. */
export function reportHref(path: string, state: ReportState, saved?: string) {
  const href = hrefFor(path, state);
  return saved ? `${href}${href.includes("?") ? "&" : "?"}saved=${encodeURIComponent(saved)}` : href;
}

/**
 * A link to a prototype page: the filters, the trail of the drill-down and the saved filter.
 * The rows, the columns and the open rows of the pivot stay out.
 */
export function pageHref(path: string, state: ReportState, options: { trail?: TrailStep[]; saved?: string; extra?: Record<string, string> } = {}) {
  const query = stateQuery({ ...state, rows: ["rep"], columns: "months", open: [], sort: "value" }, false);
  query.delete("rows");
  if (options.trail && options.trail.length > 0) {
    query.set("trail", trailText(options.trail));
  }
  if (options.saved) {
    query.set("saved", options.saved);
  }
  for (const [name, value] of Object.entries(options.extra ?? {})) {
    query.set(name, value);
  }
  const text = query.toString();
  return text ? `${path}?${text}` : path;
}
