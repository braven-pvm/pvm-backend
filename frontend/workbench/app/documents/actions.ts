"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { fileDocumentTo, rejectDocument } from "../../src/api/documents";
import { fileToErrorMessage, isDocumentId, rejectErrorMessage, unconfirmedMessage } from "../../src/documents";

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
