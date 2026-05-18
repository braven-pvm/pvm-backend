"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { refreshInvoiceCandidates, submitInvoice } from "../src/api/client";

export async function refreshCandidatesAction() {
  const candidate = await refreshInvoiceCandidates();
  revalidatePath("/invoices");
  redirect(`/invoices/${candidate.id}`);
}

export async function submitInvoiceAction(formData: FormData) {
  const id = formData.get("id");

  if (typeof id !== "string" || id.length === 0) {
    throw new Error("Invoice candidate id is required.");
  }

  await submitInvoice(id);
  revalidatePath("/invoices");
  revalidatePath(`/invoices/${id}`);
  redirect(`/invoices/${id}`);
}
