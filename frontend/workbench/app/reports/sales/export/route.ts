import { fetchPivotFile } from "../../../../src/api/sales-report";
import { getCurrentUser, hasAnyRole } from "../../../../src/auth/session";
import { apiQuery, readState } from "../../../../src/sales-report";

const text = { "Content-Type": "text/plain; charset=utf-8" };

/**
 * Passes the Excel file of the sales pivot from the API to the browser, with the user's sign-in.
 * All levels are in it. format=csv gives the CSV file.
 */
export async function GET(request: Request) {
  const user = await getCurrentUser();
  if (!user || !hasAnyRole(user, ["Admin", "Operator"])) {
    return new Response("Sign in as an Admin or an Operator.", { status: 403, headers: text });
  }

  const search: Record<string, string[]> = {};
  for (const [name, value] of new URL(request.url).searchParams) {
    (search[name] ??= []).push(value);
  }
  const format = search.format?.[0] === "csv" ? "csv" : "xlsx";
  const response = await fetchPivotFile(apiQuery(readState(search)), format);
  if (!response.ok) {
    const problem = (await response.json().catch(() => ({}))) as { message?: string };
    return new Response(problem.message ?? "The sales report could not be exported.", { status: response.status, headers: text });
  }

  return new Response(await response.arrayBuffer(), {
    headers: {
      "Content-Type": format === "csv" ? "text/csv; charset=utf-8" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
      "Content-Disposition": response.headers.get("Content-Disposition") ?? `attachment; filename="sales-report.${format}"`,
    },
  });
}
