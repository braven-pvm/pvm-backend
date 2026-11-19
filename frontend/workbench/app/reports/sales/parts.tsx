import { redirect } from "next/navigation";
import { hasAnyRole, requireWorkbenchUser } from "../../../src/auth/session";
import { formatStatementDate } from "../../../src/formatters.mjs";
export { reportHref } from "./links";

/*
 * The parts that the sales report pages share. Refer to docs/design/sales-report-pages.md.
 */

export type Search = Record<string, string | string[] | undefined>;

export const basePath = "/reports/sales";

export async function requireReportUser(path: string) {
  const user = await requireWorkbenchUser(path);
  if (!hasAnyRole(user, ["Admin", "Operator"])) {
    redirect("/");
  }
  return user;
}

export function single(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

/** "Sales from the load of 18 Nov 2026 at 02:05." */
export function freshness(dataTo: string | null | undefined) {
  if (!dataTo) {
    return "No sales load has finished yet.";
  }
  const time = new Date(dataTo);
  const clock = time.toLocaleTimeString("en-ZA", { hour: "2-digit", minute: "2-digit", hour12: false, timeZone: "Africa/Johannesburg" });
  return `Sales from the load of ${formatStatementDate(dataTo.slice(0, 10))} at ${clock}.`;
}

/** True when the last load is more than 36 hours old: the nightly load did not run. */
export function isStale(dataTo: string | null | undefined, now = new Date()) {
  return !dataTo || now.getTime() - new Date(dataTo).getTime() > 36 * 60 * 60 * 1000;
}
