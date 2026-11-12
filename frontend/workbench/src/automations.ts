/*
 * The automation registry. Every job that runs without a person is listed
 * here, with its schedule and how its health is worked out.
 *
 * This is the frontend registry of step C2. The backend has no registry
 * endpoint yet, so modes and schedules here mirror the worker configuration
 * (backend/src/Pvm.Worker/Program.cs). A later backend step moves them to
 * GET /api/automations; the page code then reads that instead.
 */

export type AutomationKey =
  | "shoprite-submission"
  | "invoice-reading"
  | "invoice-lookback"
  | "investec-feed"
  | "shoprite-po-refresh"
  | "stock-sync";

export type Mode = "Off" | "Shadow" | "Live" | "Planned";
export type Health = "Healthy" | "Late" | "Failing" | "Blocked" | "Off" | "Unknown";

export type AutomationDefinition = {
  key: AutomationKey;
  name: string;
  module: "Shoprite" | "Banking" | "Web store";
  description: string;
  schedule: string;
  /** Minutes between runs, for the Late check. */
  intervalMinutes?: number;
  /** How the backend records a run of this automation, when it records one. */
  runFilter?: { runType: string; trigger?: string; excludeTrigger?: string };
  /** The fixed mode, for automations whose mode is not a policy setting. */
  mode?: Mode;
  /** Why the mode is fixed, shown on the record page. */
  modeNote?: string;
};

export const automations: AutomationDefinition[] = [
  {
    key: "shoprite-submission",
    name: "Shoprite submission",
    module: "Shoprite",
    description: "Sends invoices that pass every check to the Shoprite B2B portal.",
    schedule: "When an invoice is ready",
  },
  {
    key: "invoice-reading",
    name: "Invoice reading",
    module: "Shoprite",
    description: "Reads new and changed Shoprite invoices from Acumatica and checks each one.",
    schedule: "Every 10 minutes",
    intervalMinutes: 10,
    runFilter: { runType: "acumatica-invoice-reconciliation", excludeTrigger: "daily-lookback" },
    mode: "Live",
    modeNote: "Reading writes nothing to Acumatica or Shoprite, so it always runs.",
  },
  {
    key: "invoice-lookback",
    name: "Invoice lookback",
    module: "Shoprite",
    description: "Reads the last 7 days of invoices again, to catch anything the 10-minute read missed.",
    schedule: "Daily",
    intervalMinutes: 24 * 60,
    runFilter: { runType: "acumatica-invoice-reconciliation", trigger: "daily-lookback" },
    mode: "Live",
    modeNote: "Reading writes nothing to Acumatica or Shoprite, so it always runs.",
  },
  {
    key: "investec-feed",
    name: "Investec feed",
    module: "Banking",
    description: "Reads Investec transactions and creates bank statements in Acumatica.",
    schedule: "Daily at 05:00",
    intervalMinutes: 24 * 60,
    mode: "Live",
    modeNote: "The feed runs as its own scheduled job. It records its runs in the logs, not in Runs.",
  },
  {
    key: "shoprite-po-refresh",
    name: "Shoprite order reading",
    module: "Shoprite",
    description: "Read purchase orders from the Shoprite portal.",
    schedule: "Switched off",
    mode: "Off",
    modeNote: "Any read takes the orders off the Shoprite portal, so order reading stays off.",
  },
  {
    key: "stock-sync",
    name: "Stock sync",
    module: "Web store",
    description: "Publishes Acumatica stock levels to the pvm.co.za web store.",
    schedule: "Every 30 minutes, when built",
    mode: "Planned",
    modeNote: "Not built yet. The web store blocks API calls until the host allows /wp-json/wc/.",
  },
];

export function findAutomation(key: string) {
  return automations.find((automation) => automation.key === key);
}

type RunLike = { runType: string; trigger: string; status: string; createdAt: string; completedAt?: string | null };

/** The runs that belong to an automation, newest first. */
export function runsFor<T extends RunLike>(definition: AutomationDefinition, runs: T[]): T[] {
  const filter = definition.runFilter;
  if (!filter) {
    return [];
  }
  return runs
    .filter(
      (run) =>
        run.runType === filter.runType &&
        (!filter.trigger || run.trigger === filter.trigger) &&
        (!filter.excludeTrigger || run.trigger !== filter.excludeTrigger),
    )
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt));
}

/**
 * Health from the runs: Failing when the latest finished run failed, Late when
 * the latest success is older than two intervals, Healthy otherwise.
 */
export function healthFromRuns(definition: AutomationDefinition, runs: RunLike[], now: Date): Health {
  if (definition.mode === "Off" || definition.mode === "Planned") {
    return "Off";
  }
  const own = runsFor(definition, runs);
  const finished = own.filter((run) => run.status === "Succeeded" || run.status === "Failed");
  if (finished.length === 0) {
    return "Unknown";
  }
  if (finished[0].status === "Failed") {
    return "Failing";
  }
  if (definition.intervalMinutes) {
    const last = new Date(finished[0].completedAt ?? finished[0].createdAt).getTime();
    if (now.getTime() - last > 2 * definition.intervalMinutes * 60_000) {
      return "Late";
    }
  }
  return "Healthy";
}

/**
 * Health for a feed that reports only the last day it imported. Weekends
 * have no bank lines, so the feed is Late only after 3 days.
 */
export function healthFromImportedThrough(importedThrough: string | null | undefined, now: Date): Health {
  if (!importedThrough) {
    return "Unknown";
  }
  const days = (now.getTime() - new Date(importedThrough).getTime()) / 86_400_000;
  return days > 3 ? "Late" : "Healthy";
}

/** The Shoprite policy mode in the Off, Shadow, Live words of the registry. */
export function modeFromPolicy(policyMode: string): Mode {
  if (policyMode === "Shadow") return "Shadow";
  if (policyMode === "Allowlisted" || policyMode === "Enabled") return "Live";
  return "Off";
}
