"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { deleteDocument, fileDocumentTo, getReviewList, processDocumentAgain, rejectDocument, returnDocumentToReview } from "../../src/api/documents";
import { afterActionHref, documentHref, findNeighbours, listHref, parseListContext, type ListContext, type ListNeighbour } from "../../src/document-list";
import {
  deleteErrorMessage,
  documentDeletedMessage,
  fileToErrorMessage,
  maxDeleteReasonLength,
  isDocumentId,
  processAgainErrorMessage,
  rejectErrorMessage,
  returnToReviewErrorMessage,
  unconfirmedMessage,
} from "../../src/documents";

function documentId(formData: FormData): string {
  const id = formData.get("documentId");
  if (!isDocumentId(id)) {
    throw new Error("The document id is missing.");
  }
  return id;
}

function text(formData: FormData, name: string): string {
  const value = formData.get(name);
  return typeof value === "string" ? value.trim() : "";
}

/** The list the page was opened from, sent as hidden fields (src/document-list.ts). */
function listContext(formData: FormData): ListContext | null {
  const params = new URLSearchParams();
  for (const [key, value] of formData.entries()) {
    if (typeof value === "string") {
      params.append(key, value);
    }
  }
  return parseListContext(params);
}

/**
 * The document after this one on the review list, looked up before the action
 * changes the list. Null outside the review list, at its end, or when the list
 * does not load: the user then goes back to the list.
 */
async function nextOnReviewList(id: string, context: ListContext | null): Promise<ListNeighbour | null> {
  if (context?.kind !== "review") {
    return null;
  }
  try {
    return (await findNeighbours(id, context.page, getReviewList))?.next ?? null;
  } catch (error) {
    console.warn("documents.next.unavailable", { error: String(error) });
    return null;
  }
}

function revalidate(id: string) {
  revalidatePath("/documents");
  revalidatePath("/documents/review");
  revalidatePath(`/documents/${id}`);
}

/** Back to the document page with the error. A 409 also reloads it, so the user sees the new state. */
function fail(id: string, context: ListContext | null, error: string): never {
  revalidate(id);
  redirect(documentHref(id, context, { documentError: error }));
}

/** On to where the action leads (src/document-list.ts afterActionHref), with the notice. */
function succeed(
  id: string,
  context: ListContext | null,
  notice: Record<string, string>,
  leave: "next" | "list" | "stay",
  next: ListNeighbour | null = null,
): never {
  revalidate(id);
  redirect(afterActionHref({ id, context, next, notice, leave }));
}

export async function fileDocumentToAction(formData: FormData) {
  const id = documentId(formData);
  const context = listContext(formData);
  const invoiceReference = text(formData, "invoiceReference");
  if (!invoiceReference || invoiceReference.length > 64) {
    fail(id, context, fileToErrorMessage(400, invoiceReference || "(none)"));
  }

  const next = await nextOnReviewList(id, context);
  let result: Awaited<ReturnType<typeof fileDocumentTo>>;
  try {
    result = await fileDocumentTo(id, invoiceReference);
  } catch {
    // A timeout or a network error: the backend may have filed it. Never guess.
    fail(id, context, unconfirmedMessage("filed"));
  }
  if (!result.ok) {
    fail(id, context, fileToErrorMessage(result.status, invoiceReference));
  }
  succeed(id, context, { documentStatus: `Document filed to ${invoiceReference}.` }, "next", next);
}

export async function rejectDocumentAction(formData: FormData) {
  const id = documentId(formData);
  const context = listContext(formData);
  const note = text(formData, "note");
  if (!note || note.length > 1000) {
    fail(id, context, rejectErrorMessage(400));
  }

  const next = await nextOnReviewList(id, context);
  let result: Awaited<ReturnType<typeof rejectDocument>>;
  try {
    result = await rejectDocument(id, note);
  } catch {
    fail(id, context, unconfirmedMessage("rejected"));
  }
  if (!result.ok) {
    fail(id, context, rejectErrorMessage(result.status));
  }
  succeed(id, context, { documentStatus: "Document rejected. It is off the review list." }, "next", next);
}

export async function returnToReviewAction(formData: FormData) {
  const id = documentId(formData);
  const context = listContext(formData);
  const note = text(formData, "note");
  if (!note || note.length > 1000) {
    fail(id, context, returnToReviewErrorMessage(400));
  }

  let result: Awaited<ReturnType<typeof returnDocumentToReview>>;
  try {
    result = await returnDocumentToReview(id, note);
  } catch {
    fail(id, context, unconfirmedMessage("sent back to review"));
  }
  if (!result.ok) {
    fail(id, context, returnToReviewErrorMessage(result.status));
  }
  succeed(id, context, { documentStatus: "Document sent back to review. It is on the review list." }, "stay");
}

export async function processAgainAction(formData: FormData) {
  const id = documentId(formData);
  const context = listContext(formData);

  let result: Awaited<ReturnType<typeof processDocumentAgain>>;
  try {
    result = await processDocumentAgain(id);
  } catch {
    fail(id, context, unconfirmedMessage("queued for processing"));
  }
  if (!result.ok) {
    fail(id, context, processAgainErrorMessage(result.status));
  }
  succeed(id, context, { documentStatus: "Document queued. It will be read and matched again in a minute or two." }, "stay");
}

/**
 * Deletes the document for good. From the review list the next document opens
 * (or the list, at its end); otherwise the list it came from, with the notice.
 */
export async function deleteDocumentAction(formData: FormData) {
  const id = documentId(formData);
  const context = listContext(formData);
  const reason = text(formData, "reason");
  if (!reason || reason.length > maxDeleteReasonLength) {
    fail(id, context, deleteErrorMessage(400));
  }

  const next = await nextOnReviewList(id, context);
  let result: Awaited<ReturnType<typeof deleteDocument>>;
  try {
    result = await deleteDocument(id, reason);
  } catch {
    fail(id, context, unconfirmedMessage("deleted"));
  }
  if (!result.ok) {
    if (result.status === 404) {
      // Gone already: the document page would only show "not found".
      revalidatePath("/documents");
      revalidatePath("/documents/review");
      redirect(listHref(context, { documentError: deleteErrorMessage(404) }));
    }
    fail(id, context, deleteErrorMessage(result.status, result.code));
  }

  const notice: Record<string, string> = { documentStatus: documentDeletedMessage };
  if (result.warning) {
    notice.documentWarning = result.warning;
  }
  succeed(id, context, notice, "list", next);
}
