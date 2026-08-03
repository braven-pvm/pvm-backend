"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { createUser, updateUserRoles, updateUserStatus } from "../src/api/admin";
import {
  refreshInvoiceCandidates,
  refreshPurchaseOrders,
  saveInvoiceLineMapping,
  seedInvoiceCandidateFromPurchaseOrder,
  submitInvoice,
  enqueueIntegrationCommand,
} from "../src/api/client";

export async function refreshCandidatesAction() {
  await refreshInvoiceCandidates();
  revalidatePath("/invoices");
  redirect("/invoices");
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

export async function saveInvoiceLineMappingAction(formData: FormData) {
  const id = requiredString(formData, "id");
  const lineNumber = Number(requiredString(formData, "lineNumber"));
  const purchaseOrderLineId = requiredString(formData, "purchaseOrderLineId");
  const shopriteUom = requiredShopriteUom(formData, "shopriteUom");

  if (!Number.isInteger(lineNumber) || lineNumber <= 0) {
    throw new Error("lineNumber must be a positive integer.");
  }

  await saveInvoiceLineMapping(id, lineNumber, {
    purchaseOrderLineId,
    shopriteUom,
  });
  revalidatePath("/invoices");
  revalidatePath(`/invoices/${id}`);
  redirect(`/invoices/${id}`);
}

export async function refreshPurchaseOrdersAction() {
  await refreshPurchaseOrders();
  revalidatePath("/purchase-orders");
  redirect("/purchase-orders");
}

export async function seedInvoiceFromPurchaseOrderAction(formData: FormData) {
  const id = requiredString(formData, "id");
  const candidate = await seedInvoiceCandidateFromPurchaseOrder(id);

  revalidatePath("/invoices");
  revalidatePath("/purchase-orders");
  revalidatePath(`/purchase-orders/${id}`);
  redirect(`/invoices/${candidate.id}`);
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

export async function enqueueIntegrationCommandAction(formData: FormData) {
  const command = requiredString(formData, "command");
  if (command !== "acumatica-discovery" && command !== "shoprite-po-refresh") {
    throw new Error("Unsupported integration command.");
  }

  await enqueueIntegrationCommand(command);
  revalidatePath("/admin/messages");
  redirect("/admin/messages");
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

function requiredShopriteUom(formData: FormData, key: string) {
  const value = requiredString(formData, key);
  if (!["EA", "CA", "CS", "KG"].includes(value)) {
    throw new Error(`${key} is invalid.`);
  }

  return value as "EA" | "CA" | "CS" | "KG";
}
