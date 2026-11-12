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
  assignException,
  commentOnException,
  setExceptionStatus,
  resolveAmbiguousSubmission,
  holdInvoice,
  releaseInvoice,
  retrySubmission,
  replayDeadLetter,
  resolveDeadLetters,
  importNedbankStatement,
  previewNedbankStatement,
  type AutomationMode,
} from "../src/api/client";

export async function refreshCandidatesAction() {
  await refreshInvoiceCandidates();
  revalidatePath("/shoprite/invoices");
  redirect("/shoprite/invoices");
}

export async function submitInvoiceAction(formData: FormData) {
  const id = formData.get("id");

  if (typeof id !== "string" || id.length === 0) {
    throw new Error("Invoice candidate id is required.");
  }

  const result = await submitInvoice(id);
  revalidatePath("/shoprite/invoices");
  revalidatePath(`/shoprite/invoices/${id}`);
  const successful = ["Submitted", "InProgress", "Ambiguous"].includes(result.status);
  redirect(`/shoprite/invoices/${id}?${successful ? "submissionStatus" : "submissionError"}=${encodeURIComponent(result.message)}`);
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
      `/shoprite/settings/inventory-mappings/new?mappingError=${encodeURIComponent(errorMessage)}&newSku=${encodeURIComponent(inventoryId)}&uom=${encodeURIComponent(acumaticaUom)}`,
    );
  }

  revalidatePath("/shoprite/invoices");
  revalidatePath("/shoprite/settings/inventory-mappings");
  redirect(
    `/shoprite/settings/inventory-mappings?mappingStatus=${encodeURIComponent(`${inventoryId} mapping saved and invoice candidates revalidated.`)}&search=${encodeURIComponent(inventoryId)}`,
  );
}

export async function refreshPurchaseOrdersAction() {
  const run = await refreshPurchaseOrders();
  revalidatePath("/shoprite/purchase-orders");
  revalidatePath("/runs");
  redirect(`/runs/${run.runId}`);
}

export async function seedInvoiceFromPurchaseOrderAction(formData: FormData) {
  const id = requiredString(formData, "id");
  const candidate = await seedInvoiceCandidateFromPurchaseOrder(id);

  revalidatePath("/shoprite/invoices");
  revalidatePath("/shoprite/purchase-orders");
  revalidatePath(`/shoprite/purchase-orders/${id}`);
  redirect(`/shoprite/invoices/${candidate.id}`);
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
  revalidatePath("/events");

  // The form returns to its own page. Only an automation page or Events is accepted.
  const returnTo = formData.get("returnTo");
  const target = typeof returnTo === "string" && /^\/(automations\/[\w-]+|events)$/.test(returnTo) ? returnTo : "/events";
  revalidatePath(target);
  redirect(`${target}?queued=1`);
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
  revalidatePath("/automations/shoprite-submission");
  redirect(errorMessage
    ? `/automations/shoprite-submission?policyError=${encodeURIComponent(errorMessage)}`
    : "/automations/shoprite-submission?policyStatus=Automation%20policy%20saved%20and%20candidates%20evaluated.");
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
  revalidatePath("/automations/shoprite-submission");
  redirect(errorMessage
    ? `/automations/shoprite-submission?policyError=${encodeURIComponent(errorMessage)}`
    : "/automations/shoprite-submission?policyStatus=Emergency%20control%20updated.");
}

async function runExceptionAction(action: () => Promise<void>, formData?: FormData) {
  let errorMessage: string | undefined;
  try {
    await action();
  } catch (error) {
    errorMessage = error instanceof Error ? error.message : "The exception action failed.";
  }

  // A form on an exception's own page returns there. Only exception paths are accepted.
  const returnTo = formData?.get("returnTo");
  const target = typeof returnTo === "string" && /^\/exceptions(\/[\w-]+)?$/.test(returnTo) ? returnTo : "/exceptions";

  revalidatePath("/exceptions");
  revalidatePath("/shoprite/invoices");
  redirect(errorMessage
    ? `${target}?exceptionError=${encodeURIComponent(errorMessage)}`
    : `${target}?exceptionStatus=Exception%20updated.`);
}

export async function assignExceptionAction(formData: FormData) {
  const id = requiredString(formData, "id");
  const owner = optionalString(formData, "owner") ?? null;
  await runExceptionAction(() => assignException(id, owner), formData);
}

export async function commentOnExceptionAction(formData: FormData) {
  const id = requiredString(formData, "id");
  const body = requiredString(formData, "body");
  await runExceptionAction(() => commentOnException(id, body), formData);
}

export async function setExceptionStatusAction(formData: FormData) {
  const id = requiredString(formData, "id");
  const status = requiredString(formData, "status");
  const reason = requiredString(formData, "reason");
  await runExceptionAction(() => setExceptionStatus(id, status, reason), formData);
}

export async function resolveAmbiguousSubmissionAction(formData: FormData) {
  const submissionOperationId = requiredString(formData, "submissionOperationId");
  const outcome = requiredString(formData, "outcome");
  const evidence = requiredString(formData, "evidence");
  const reason = requiredString(formData, "reason");
  await runExceptionAction(() =>
    resolveAmbiguousSubmission(submissionOperationId, outcome, evidence, reason), formData);
}

export async function holdInvoiceAction(formData: FormData) {
  const id = requiredString(formData, "invoiceCandidateId");
  const reason = requiredString(formData, "reason");
  await runExceptionAction(() => holdInvoice(id, reason), formData);
}

export async function releaseInvoiceAction(formData: FormData) {
  const id = requiredString(formData, "invoiceCandidateId");
  const reason = requiredString(formData, "reason");
  await runExceptionAction(() => releaseInvoice(id, reason), formData);
}

export async function retrySubmissionAction(formData: FormData) {
  const id = requiredString(formData, "invoiceCandidateId");
  const reason = requiredString(formData, "reason");
  await runExceptionAction(() => retrySubmission(id, reason), formData);
}

export async function replayDeadLetterAction(formData: FormData) {
  const deliveryId = requiredString(formData, "deliveryId");
  const reason = requiredString(formData, "reason");
  await runExceptionAction(() => replayDeadLetter(deliveryId, reason), formData);
}

export async function resolveDeadLettersAction(formData: FormData) {
  const reason = requiredString(formData, "reason");
  const queueName = optionalString(formData, "queueName") ?? null;
  const olderThanDays = requiredInteger(formData, "olderThanDays");
  await runExceptionAction(() => resolveDeadLetters(reason, queueName, olderThanDays), formData);
}

// The bank import screens keep the chosen file in the browser and render the outcome in
// place, so these two actions return a result instead of redirecting.
export async function previewNedbankStatementAction(formData: FormData) {
  const file = readStatementFile(formData);
  if (!file) {
    return { ok: false as const, message: "Choose a statement file first." };
  }

  try {
    return { ok: true as const, preview: await previewNedbankStatement(file) };
  } catch (error) {
    return { ok: false as const, message: describeBankImportFailure(error) };
  }
}

export async function importNedbankStatementAction(formData: FormData) {
  const file = readStatementFile(formData);
  if (!file) {
    return { ok: false as const, message: "Choose a statement file first." };
  }

  try {
    const result = await importNedbankStatement(file);
    revalidatePath("/banking");
    return { ok: true as const, result };
  } catch (error) {
    return { ok: false as const, message: describeBankImportFailure(error) };
  }
}

function readStatementFile(formData: FormData) {
  const file = formData.get("file");
  return file instanceof File && file.size > 0 ? file : null;
}

function describeBankImportFailure(error: unknown) {
  return error instanceof Error ? error.message : "The statement could not be read.";
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
