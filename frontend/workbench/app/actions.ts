"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { createUser, updateUserRoles, updateUserStatus } from "../src/api/admin";
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

export async function createUserAction(formData: FormData) {
  const email = requiredString(formData, "email");
  const displayName = optionalString(formData, "displayName");
  const roles = formData.getAll("roles").map(String);

  await createUser({ email, displayName, roles });
  revalidatePath("/admin/users");
  redirect("/admin/users");
}

export async function updateUserRolesAction(formData: FormData) {
  const id = requiredString(formData, "id");
  const roles = formData.getAll("roles").map(String);

  await updateUserRoles(id, roles);
  revalidatePath("/admin/users");
  redirect("/admin/users");
}

export async function updateUserStatusAction(formData: FormData) {
  const id = requiredString(formData, "id");
  const status = requiredString(formData, "status");

  await updateUserStatus(id, status);
  revalidatePath("/admin/users");
  redirect("/admin/users");
}

function requiredString(formData: FormData, key: string) {
  const value = formData.get(key);
  if (typeof value !== "string" || value.length === 0) {
    throw new Error(`${key} is required.`);
  }

  return value;
}

function optionalString(formData: FormData, key: string) {
  const value = formData.get(key);
  return typeof value === "string" && value.length > 0 ? value : undefined;
}
