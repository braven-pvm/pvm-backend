import { fetchSalesReportCsv, type SalesReportView } from "../../../../src/api/sales-report";
import { getCurrentUser, hasAnyRole } from "../../../../src/auth/session";
import { reportQuery } from "../../../../src/sales-report";

const views: SalesReportView[] = ["by-rep", "by-product", "customers"];

/** Passes the CSV file of a sales report view from the API to the browser, with the user's sign-in. */
export async function GET(request: Request) {
  const user = await getCurrentUser();
  if (!user || !hasAnyRole(user, ["Admin", "Operator"])) {
    return new Response("Sign in as an Admin or an Operator.", { status: 403, headers: { "Content-Type": "text/plain; charset=utf-8" } });
  }

  const search = Object.fromEntries(new URL(request.url).searchParams);
  const view = views.find((item) => item === search.view);
  if (!view) {
    return new Response("There is no such sales report view.", { status: 404, headers: { "Content-Type": "text/plain; charset=utf-8" } });
  }

  const response = await fetchSalesReportCsv(view, reportQuery(search));
  if (!response.ok) {
    const problem = (await response.json().catch(() => ({}))) as { message?: string };
    return new Response(problem.message ?? "The sales report could not be exported.", { status: response.status, headers: { "Content-Type": "text/plain; charset=utf-8" } });
  }

  return new Response(await response.arrayBuffer(), {
    headers: {
      "Content-Type": "text/csv; charset=utf-8",
      "Content-Disposition": response.headers.get("Content-Disposition") ?? `attachment; filename="sales-${view}.csv"`,
    },
  });
}
