import { getApiAuthHeaders, requireWorkbenchUser } from "../../../src/auth/session";
import { timedFetch } from "../../../src/api/timed-fetch";

const apiBaseUrl = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5000";

/** Passes the Excel file of a statement from the API to the browser, with the user's sign-in. */
export async function GET(request: Request) {
  await requireWorkbenchUser("/statements");
  const query = new URL(request.url).searchParams;
  const allowed = new URLSearchParams();
  for (const key of ["company", "from", "to"]) {
    const value = query.get(key);
    if (value) {
      allowed.set(key, value);
    }
  }

  const response = await timedFetch(`${apiBaseUrl}/api/reports/statements/export?${allowed}`, {
    headers: await getApiAuthHeaders(),
    cache: "no-store",
  });
  if (!response.ok) {
    const problem = (await response.json().catch(() => ({}))) as { message?: string };
    return new Response(problem.message ?? "The statement could not be exported.", { status: response.status, headers: { "Content-Type": "text/plain; charset=utf-8" } });
  }

  return new Response(await response.arrayBuffer(), {
    headers: {
      "Content-Type": response.headers.get("Content-Type") ?? "application/octet-stream",
      "Content-Disposition": response.headers.get("Content-Disposition") ?? 'attachment; filename="statement.xlsx"',
    },
  });
}
