/**
 * fetch with one log line per API call: method, path, status and duration.
 * The line goes to the container console, so production logs show which
 * endpoint makes a page slow. The path never includes the query string, so
 * no search text or identifier in it reaches the logs.
 */
export async function timedFetch(input: string, init?: RequestInit): Promise<Response> {
  const started = performance.now();
  const method = init?.method ?? "GET";
  const path = safePath(input);
  try {
    const response = await fetch(input, init);
    console.info("api.call", { method, path, status: response.status, ms: Math.round(performance.now() - started) });
    return response;
  } catch (error) {
    console.warn("api.call.failed", { method, path, ms: Math.round(performance.now() - started), error: String(error) });
    throw error;
  }
}

function safePath(url: string) {
  try {
    // Ids in the path become ":id", so the log groups calls by endpoint.
    return new URL(url).pathname.replace(/\/[0-9a-f-]{16,}(?=\/|$)/gi, "/:id");
  } catch {
    return "unknown";
  }
}
