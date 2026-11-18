import { fetchDocumentThumbnail } from "../../../../src/api/documents";
import { isDocumentId } from "../../../../src/documents";

/*
 * The phone's JPEG of a document's first page, for the General scans list.
 * Like the PDF route, the workbench asks the backend with the signed-in user's
 * token on the server; the backend URL and the token never reach the browser.
 */

export const dynamic = "force-dynamic";

export async function GET(_request: Request, { params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  if (!isDocumentId(id)) {
    return new Response("Not found", { status: 404 });
  }

  const upstream = await fetchDocumentThumbnail(id);
  if (!upstream) {
    return new Response("Sign in to see this document.", { status: 401 });
  }
  if (!upstream.ok || !upstream.body) {
    const status = [401, 403, 404].includes(upstream.status) ? upstream.status : 502;
    return new Response("The thumbnail could not be loaded.", { status });
  }

  return new Response(upstream.body, {
    status: 200,
    headers: {
      "Content-Type": "image/jpeg",
      // A picture of a private paper: the browser may keep it briefly, no shared cache.
      "Cache-Control": "private, max-age=300",
      "X-Content-Type-Options": "nosniff",
    },
  });
}
