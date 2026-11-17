import { fetchDocumentFile } from "../../../../src/api/documents";

/*
 * The PDF of one document, for the <iframe> on the document page. The
 * browser asks the workbench; the workbench asks the backend with the
 * signed-in user's token on the server, as every page does. The backend URL
 * and the token never reach the browser.
 */

export const dynamic = "force-dynamic";

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export async function GET(_request: Request, { params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  if (!guid.test(id)) {
    return new Response("Not found", { status: 404 });
  }

  const upstream = await fetchDocumentFile(id);
  if (!upstream) {
    return new Response("Sign in to see this document.", { status: 401 });
  }
  if (!upstream.ok || !upstream.body) {
    // 401, 403 and 404 pass through; anything else is the backend's problem, not the browser's.
    const status = [401, 403, 404].includes(upstream.status) ? upstream.status : 502;
    return new Response("The document could not be loaded.", { status });
  }

  return new Response(upstream.body, {
    status: 200,
    headers: {
      "Content-Type": "application/pdf",
      "Content-Disposition": upstream.headers.get("Content-Disposition") ?? `inline; filename="document-${id}.pdf"`,
      // The PDF is a client's delivery record: no shared cache keeps it.
      "Cache-Control": "private, no-store",
      "X-Content-Type-Options": "nosniff",
    },
  });
}
