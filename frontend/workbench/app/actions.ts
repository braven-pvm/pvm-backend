"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { createUser, updateUserRoles, updateUserStatus } from "../src/api/admin";
import {
  refreshInvoiceCandidates,
  refreshPurchaseOrders,
  saveInventoryMapping,
  seedInvoiceCandidateFromPurchaseOrder,
  submitInvoice,
  enqueueIntegrationCommand,
  changeAutomationPolicy,
  setAutomationEmergencyStop,
  type AutomationMode,
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

  const result = await submitInvoice(id);
  revalidatePath("/invoices");
  revalidatePath(`/invoices/${id}`);
  const successful = ["Submitted", "InProgress", "Ambiguous"].includes(result.status);
  redirect(`/invoices/${id}?${successful ? "submissionStatus" : "submissionError"}=${encodeURIComponent(result.message)}`);
}

export async function saveInventoryMappingAction(formData: FormData) {
  const inventoryId = requiredString(formData, "inventoryId");
  const acumaticaUom = requiredString(formData, "acumaticaUom");
  const purchaseOrderLineId = requiredString(formData, "purchaseOrderLineId");
  const shopriteUom = requiredShopriteUom(formData, "shopriteUom");
  const reason = requiredString(formData, "reason");

  let errorMessage: string | undefined;
  try {
    await saveInventoryMapping(inventoryId, acumaticaUom, {
      purchaseOrderLineId,
      shopriteUom,
      reason,
    });
  } catch (error) {
    errorMessage = error instanceof Error ? error.message : "Inventory mapping could not be saved.";
  }

  if (errorMessage) {
    redirect(
      `/admin/inventory-mappings?mappingError=${encodeURIComponent(errorMessage)}&newSku=${encodeURIComponent(inventoryId)}#add-mapping`,
    );
  }

  revalidatePath("/invoices");
  revalidatePath("/admin/inventory-mappings");
  redirect(
    `/admin/inventory-mappings?mappingStatus=${encodeURIComponent(`${inventoryId} mapping saved and invoice candidates revalidated.`)}&search=${encodeURIComponent(inventoryId)}`,
  );
}

export async function refreshPurchaseOrdersAction() {
  const run = await refreshPurchaseOrders();
  revalidatePath("/purchase-orders");
  revalidatePath("/runs");
  redirect(`/runs/${run.runId}`);
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

export async function changeAutomationPolicyAction(formData: FormData) {
  const expectedVersion = requiredInteger(formData, "expectedVersion");
  const mode = requiredAutomationMode(formData, "mode");
  let errorMessage: string | undefined;
  try {
    await changeAutomationPolicy({
      expectedVersion,
      mode,
      accountAllowlist: csv(formData, "accountAllowlist"),
      locationAllowlist: csv(formData, "locationAllowlist"),
      supportedOrderTypes: csv(formData, "supportedOrderTypes"),
      stabilizationDelayMinutes: requiredInteger(formData, "stabilizationDelayMinutes"),
      purchaseOrderFreshnessMinutes: requiredInteger(formData, "purchaseOrderFreshnessMinutes"),
      acumaticaFreshnessMinutes: requiredInteger(formData, "acumaticaFreshnessMinutes"),
      dailyAutomaticSubmissionCap: requiredInteger(formData, "dailyAutomaticSubmissionCap"),
      automaticWindowStart: requiredString(formData, "automaticWindowStart"),
      automaticWindowEnd: requiredString(formData, "automaticWindowEnd"),
      timeZoneId: requiredString(formData, "timeZoneId"),
      reason: requiredString(formData, "reason"),
      acknowledgeAutomaticSubmissions: formData.get("acknowledgeAutomaticSubmissions") === "true",
      environmentConfirmation: optionalString(formData, "environmentConfirmation"),
      typedConfirmation: optionalString(formData, "typedConfirmation"),
    });
  } catch (error) {
    errorMessage = error instanceof Error ? error.message : "Automation policy could not be saved.";
  }

  revalidatePath("/");
  revalidatePath("/automation");
  redirect(errorMessage
    ? `/automation?policyError=${encodeURIComponent(errorMessage)}`
    : "/automation?policyStatus=Automation%20policy%20saved%20and%20candidates%20evaluated.");
}

export async function setAutomationEmergencyStopAction(formData: FormData) {
  let errorMessage: string | undefined;
  try {
    await setAutomationEmergencyStop({
      expectedVersion: requiredInteger(formData, "expectedVersion"),
      active: requiredString(formData, "active") === "true",
      reason: requiredString(formData, "reason"),
    });
  } catch (error) {
    errorMessage = error instanceof Error ? error.message : "Emergency control could not be changed.";
  }

  revalidatePath("/");
  revalidatePath("/automation");
  redirect(errorMessage
    ? `/automation?policyError=${encodeURIComponent(errorMessage)}`
    : "/automation?policyStatus=Emergency%20control%20updated.");
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

function requiredInteger(formData: FormData, key: string) {
  const value = Number(requiredString(formData, key));
  if (!Number.isInteger(value)) {
    throw new Error(`${key} must be a whole number.`);
  }
  return value;
}

function csv(formData: FormData, key: string) {
  const value = optionalString(formData, key) ?? "";
  return value.split(/[\n,]/).map((item) => item.trim()).filter(Boolean);
}

function requiredAutomationMode(formData: FormData, key: string): AutomationMode {
  const value = requiredString(formData, key);
  if (!["Disabled", "Shadow", "Allowlisted", "Enabled"].includes(value)) {
    throw new Error(`${key} is invalid.`);
  }
  return value as AutomationMode;
}
