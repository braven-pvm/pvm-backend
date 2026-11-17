"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { fileDocumentTo, rejectDocument } from "../../src/api/documents";
import { fileToErrorMessage, rejectErrorMessage } from "../../src/documents";

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function documentId(formData: FormData): string {
  const id = formData.get("documentId");
  if (typeof id !== "string" || !guid.test(id)) {
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

  const result = await fileDocumentTo(id, invoiceReference);
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

  const result = await rejectDocument(id, note);
  if (!result.ok) {
    finish(id, { error: rejectErrorMessage(result.status) });
  }
  finish(id, { status: "Document rejected. It is off the review list." });
}
