import { notFound } from "next/navigation";
import { getIntegrationRun } from "../../../src/api/client";
import { requireWorkbenchUser } from "../../../src/auth/session";
import { formatDateTime } from "../../../src/formatters.mjs";
import { KeyValueList, Notice, Panel, RecordPage, StatusBadge, Timeline } from "../../../src/ui";
import { runName, runResult } from "../../run-columns";

export const dynamic = "force-dynamic";
export const metadata = { title: "Run" };

export default async function IntegrationRunPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  await requireWorkbenchUser(`/runs/${id}`);
  const run = await loadRun(id);

  return (
    <RecordPage
      back={{ href: "/runs", label: "Runs" }}
      header={{
        eyebrow: "Run",
        title: runName(run),
        description: `${run.trigger} · ${run.initiatedBy} · started ${formatDateTime(run.startedAt ?? run.createdAt)}`,
        actions: <StatusBadge status={run.status} />,
      }}
      notice={
        run.errorSummary ? (
          <Notice tone="error" title={run.errorSummary}>
            Error code {run.errorCode ?? "not recorded"}. The run tried {run.attemptCount === 1 ? "once" : `${run.attemptCount} times`}.
          </Notice>
        ) : null
      }
      summary={
        <KeyValueList
          items={[
            { label: "Result", value: runResult(run) },
            { label: "Read", value: String(run.receivedCount) },
            { label: "New", value: String(run.createdCount) },
            { label: "Changed", value: String(run.updatedCount) },
            { label: "Unchanged", value: String(run.unchangedCount) },
            { label: "Skipped", value: String(run.skippedCount) },
            { label: "Checked again", value: String(run.revalidatedCount) },
            { label: "Failed", value: String(run.failedCount) },
          ]}
        />
      }
      activity={
        <Timeline
          label="Run activity"
          entries={[
            ...(run.completedAt ? [{ id: "completed", time: formatDateTime(run.completedAt), dateTime: run.completedAt, title: "Finished", status: <StatusBadge status={run.status} /> }] : []),
            ...(run.startedAt ? [{ id: "started", time: formatDateTime(run.startedAt), dateTime: run.startedAt, title: "Started" }] : []),
            { id: "created", time: formatDateTime(run.createdAt), dateTime: run.createdAt, title: `Queued by ${run.initiatedBy}`, detail: run.environmentName },
          ]}
        />
      }
    >
      <Panel title="Diagnostics" description="For tracing the run in the logs.">
        <KeyValueList
          items={[
            { label: "Read from", value: run.queryFrom ? formatDateTime(run.queryFrom) : null },
            { label: "Read to", value: run.queryTo ? formatDateTime(run.queryTo) : null },
            { label: "Cursor before", value: run.cursorBefore ? formatDateTime(run.cursorBefore) : null },
            { label: "Cursor after", value: run.cursorAfter ? formatDateTime(run.cursorAfter) : null },
            { label: "Run ID", value: run.id, mono: true },
            { label: "Correlation ID", value: run.correlationId, mono: true },
            { label: "Message ID", value: run.messageId, mono: true },
          ]}
        />
      </Panel>
    </RecordPage>
  );
}

async function loadRun(id: string) {
  try {
    return await getIntegrationRun(id);
  } catch {
    notFound();
  }
}
