"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { deleteDocument, fileDocumentTo, processDocumentAgain, rejectDocument, returnDocumentToReview } from "../../src/api/documents";
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

/** Back to the document page with the result. A 409 also reloads it, so the user sees the new state. */
function finish(id: string, result: { status?: string; error?: string }): never {
  revalidatePath("/documents");
  revalidatePath("/documents/review");
  revalidatePath(`/documents/${id}`);
  const query = result.error
    ? `documentError=${encodeURIComponent(result.error)}`
    : `documentStatus=${encodeURIComponent(result.status ?? "Done.")}`;
  redirect(`/documents/${id}?${query}`);
}

export async function fileDocumentToAction(formData: FormData) {
  const id = documentId(formData);
  const invoiceReference = text(formData, "invoiceReference");
  if (!invoiceReference || invoiceReference.length > 64) {
    finish(id, { error: fileToErrorMessage(400, invoiceReference || "(none)") });
  }

  let result: Awaited<ReturnType<typeof fileDocumentTo>>;
  try {
    result = await fileDocumentTo(id, invoiceReference);
  } catch {
    // A timeout or a network error: the backend may have filed it. Never guess.
    finish(id, { error: unconfirmedMessage("filed") });
  }
  if (!result.ok) {
    finish(id, { error: fileToErrorMessage(result.status, invoiceReference) });
  }
  finish(id, { status: `Document filed to ${invoiceReference}.` });
}

export async function rejectDocumentAction(formData: FormData) {
  const id = documentId(formData);
  const note = text(formData, "note");
  if (!note || note.length > 1000) {
    finish(id, { error: rejectErrorMessage(400) });
  }

  let result: Awaited<ReturnType<typeof rejectDocument>>;
  try {
    result = await rejectDocument(id, note);
  } catch {
    finish(id, { error: unconfirmedMessage("rejected") });
  }
  if (!result.ok) {
    finish(id, { error: rejectErrorMessage(result.status) });
  }
  finish(id, { status: "Document rejected. It is off the review list." });
}

export async function returnToReviewAction(formData: FormData) {
  const id = documentId(formData);
  const note = text(formData, "note");
  if (!note || note.length > 1000) {
    finish(id, { error: returnToReviewErrorMessage(400) });
  }

  let result: Awaited<ReturnType<typeof returnDocumentToReview>>;
  try {
    result = await returnDocumentToReview(id, note);
  } catch {
    finish(id, { error: unconfirmedMessage("sent back to review") });
  }
  if (!result.ok) {
    finish(id, { error: returnToReviewErrorMessage(result.status) });
  }
  finish(id, { status: "Document sent back to review. It is on the review list." });
}

export async function processAgainAction(formData: FormData) {
  const id = documentId(formData);

  let result: Awaited<ReturnType<typeof processDocumentAgain>>;
  try {
    result = await processDocumentAgain(id);
  } catch {
    finish(id, { error: unconfirmedMessage("queued for processing") });
  }
  if (!result.ok) {
    finish(id, { error: processAgainErrorMessage(result.status) });
  }
  finish(id, { status: "Document queued. It will be read and matched again in a minute or two." });
}

/** Deletes the document for good, then opens the Documents list with the notice. */
export async function deleteDocumentAction(formData: FormData) {
  const id = documentId(formData);
  const reason = text(formData, "reason");
  if (!reason || reason.length > maxDeleteReasonLength) {
    finish(id, { error: deleteErrorMessage(400) });
  }

  let result: Awaited<ReturnType<typeof deleteDocument>>;
  try {
    result = await deleteDocument(id, reason);
  } catch {
    finish(id, { error: unconfirmedMessage("deleted") });
  }
  if (!result.ok) {
    if (result.status === 404) {
      // Gone already: the document page would only show "not found".
      revalidatePath("/documents");
      redirect(`/documents?documentError=${encodeURIComponent(deleteErrorMessage(404))}`);
    }
    finish(id, { error: deleteErrorMessage(result.status, result.code) });
  }

  revalidatePath("/documents");
  revalidatePath("/documents/review");
  const query = new URLSearchParams({ documentStatus: documentDeletedMessage });
  if (result.warning) {
    query.set("documentWarning", result.warning);
  }
  redirect(`/documents?${query.toString()}`);
}
