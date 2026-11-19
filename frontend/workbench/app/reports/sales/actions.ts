"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { createSavedFilter, deleteSavedFilter, getValues, updateSavedFilter } from "../../../src/api/sales-report";
import { getCurrentUser, hasAnyRole } from "../../../src/auth/session";
import { formatFigure, isDimension, readState, stateQuery, type Measure } from "../../../src/sales-report";
import type { MultiSelectOption } from "../../../src/ui/multi-select";

const basePath = "/reports/sales";

async function requireReportAction() {
  const user = await getCurrentUser();
  if (!user || !hasAnyRole(user, ["Admin", "Operator"])) {
    throw new Error("Sign in as an Admin or an Operator.");
  }
}

function detail(total: number, measure: Measure) {
  if (measure === "customers") {
    return `${formatFigure(total)} customers`;
  }
  return measure === "amount" ? `R ${formatFigure(total)}` : formatFigure(total);
}

/** The values of a dimension for a multi-select, in the filter of the page. */
export async function loadValuesAction(dimension: string, query: string, search: string): Promise<MultiSelectOption[]> {
  await requireReportAction();
  if (!isDimension(dimension)) {
    return [];
  }
  const state = readState(Object.fromEntries(new URLSearchParams(query)));
  const answer = await getValues(dimension, stateQuery(state, false), search.slice(0, 100));
  return (answer.data ?? []).map((value) => ({ value: value.key, label: value.label, detail: detail(value.total, state.measure) }));
}

function text(formData: FormData, name: string) {
  const value = formData.get(name);
  return typeof value === "string" ? value.trim() : "";
}

/** Saves the filter of the page under a new name, then opens it. */
export async function saveFilterAction(formData: FormData) {
  await requireReportAction();
  const query = text(formData, "query");
  const answer = await createSavedFilter({ name: text(formData, "name"), query, shared: formData.get("shared") === "on" });
  revalidatePath(basePath);
  if (!answer.data) {
    redirect(`${basePath}?${query}&savedError=${encodeURIComponent(answer.message ?? "The filter could not be saved.")}`);
  }
  redirect(`${basePath}?${answer.data.query}&saved=${answer.data.id}`);
}

/** Puts the filter of the page into a saved filter, with an optional new name and sharing. */
export async function updateFilterAction(formData: FormData) {
  await requireReportAction();
  const id = text(formData, "id");
  const query = text(formData, "query");
  const answer = await updateSavedFilter(id, { name: text(formData, "name") || undefined, query, shared: formData.get("shared") === "on" });
  revalidatePath(basePath);
  if (!answer.data) {
    redirect(`${basePath}?${query}&saved=${id}&savedError=${encodeURIComponent(answer.message ?? "The filter could not be saved.")}`);
  }
  redirect(`${basePath}?${answer.data.query}&saved=${answer.data.id}`);
}

export async function deleteFilterAction(formData: FormData) {
  await requireReportAction();
  const id = text(formData, "id");
  const query = text(formData, "query");
  const answer = await deleteSavedFilter(id);
  revalidatePath(basePath);
  if (answer.message) {
    redirect(`${basePath}?${query}&saved=${id}&savedError=${encodeURIComponent(answer.message)}`);
  }
  redirect(`${basePath}?${query}`);
}
