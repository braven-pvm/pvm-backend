import type { IntegrationRun } from "../src/api/client";
import { automations, runsFor } from "../src/automations";
import { formatDateTime } from "../src/formatters.mjs";
import { StatusBadge, statusLabel, type Column } from "../src/ui";

/** The automation's name for a run, or the run type in words when no automation owns it. */
export const runName = (run: IntegrationRun) =>
  automations.find((automation) => runsFor(automation, [run]).length > 0)?.name ?? statusLabel(run.runType);

export function runResult(run: IntegrationRun) {
  if (run.errorSummary) {
    return run.errorSummary;
  }
  const parts = [
    `${run.receivedCount} read`,
    run.createdCount ? `${run.createdCount} new` : null,
    run.updatedCount ? `${run.updatedCount} changed` : null,
    run.failedCount ? `${run.failedCount} failed` : null,
  ];
  return parts.filter(Boolean).join(" · ");
}

export const runColumns: Column<IntegrationRun>[] = [
  { key: "run", header: "Run", render: runName },
  { key: "started", header: "Started", render: (run) => formatDateTime(run.startedAt ?? run.createdAt) },
  { key: "trigger", header: "Trigger", render: (run) => run.trigger, weak: true },
  { key: "status", header: "Status", render: (run) => <StatusBadge status={run.status} /> },
  { key: "result", header: "Result", render: runResult, weak: true },
];
