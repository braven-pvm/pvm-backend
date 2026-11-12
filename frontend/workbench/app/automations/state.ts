import {
  getAutomationPolicy,
  getBankingStatus,
  getIntegrationRuns,
  type AutomationPolicyView,
  type BankAccountStatus,
  type IntegrationRun,
} from "../../src/api/client";
import {
  automations,
  healthFromImportedThrough,
  healthFromRuns,
  modeFromPolicy,
  runsFor,
  type AutomationDefinition,
  type Health,
  type Mode,
} from "../../src/automations";
import { formatDateTime } from "../../src/formatters.mjs";

export type AutomationState = AutomationDefinition & {
  currentMode: Mode;
  health: Health;
  lastRun: string;
  runs: IntegrationRun[];
};

type Sources = {
  runs: IntegrationRun[];
  policy: AutomationPolicyView | null;
  investec: BankAccountStatus | undefined;
};

/** Everything the automation pages need, in one round of API calls. A source that fails leaves its automations Unknown. */
export async function loadSources(): Promise<Sources> {
  const [runs, policy, banking] = await Promise.allSettled([getIntegrationRuns(), getAutomationPolicy(), getBankingStatus()]);
  return {
    runs: runs.status === "fulfilled" ? runs.value : [],
    policy: policy.status === "fulfilled" ? policy.value : null,
    investec: banking.status === "fulfilled" ? banking.value.accounts.find((account) => account.bank === "Investec") : undefined,
  };
}

export function stateOf(definition: AutomationDefinition, sources: Sources, now = new Date()): AutomationState {
  const runs = runsFor(definition, sources.runs);

  if (definition.key === "shoprite-submission") {
    const policy = sources.policy?.policy;
    return {
      ...definition,
      currentMode: policy ? modeFromPolicy(policy.mode) : "Off",
      health: !policy ? "Unknown" : policy.emergencyStop ? "Blocked" : "Healthy",
      lastRun: policy ? `Policy v${policy.version} · ${sources.policy?.decisionSummary.wouldSubmit ?? 0} would send` : "—",
      runs,
    };
  }

  if (definition.key === "investec-feed") {
    return {
      ...definition,
      currentMode: "Live",
      health: healthFromImportedThrough(sources.investec?.importedThrough, now),
      lastRun: sources.investec?.importedThrough ? `Imported through ${formatDateTime(sources.investec.importedThrough).split(",")[0]}` : "—",
      runs,
    };
  }

  const finished = runs.find((run) => run.completedAt);
  return {
    ...definition,
    currentMode: definition.mode ?? "Off",
    health: healthFromRuns(definition, sources.runs, now),
    lastRun: finished ? formatDateTime(finished.completedAt) : "—",
    runs,
  };
}

export async function loadAutomations() {
  const sources = await loadSources();
  return automations.map((definition) => stateOf(definition, sources));
}
