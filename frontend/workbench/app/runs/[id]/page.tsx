import Link from "next/link";
import { getIntegrationRun } from "../../../src/api/client";
import { requireWorkbenchUser } from "../../../src/auth/session";

export const dynamic = "force-dynamic";

export default async function IntegrationRunPage({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = await params;
  await requireWorkbenchUser(`/runs/${id}`);
  const run = await getIntegrationRun(id);

  return (
    <main className="page-shell">
      <Link className="back-link" href="/runs">Back to runs</Link>
      <section className="page-heading">
        <div>
          <h1>{run.runType}</h1>
          <p>{run.id}</p>
        </div>
        <Status value={run.status} />
      </section>

      <section className="detail-grid">
        <div className="detail-panel">
          <h2>Execution</h2>
          <dl>
            <div><dt>Environment</dt><dd>{run.environmentName}</dd></div>
            <div><dt>Trigger</dt><dd>{run.trigger}</dd></div>
            <div><dt>Initiated by</dt><dd>{run.initiatedBy}</dd></div>
            <div><dt>Attempts</dt><dd>{run.attemptCount}</dd></div>
            <div><dt>Created</dt><dd>{new Date(run.createdAt).toLocaleString()}</dd></div>
            <div><dt>Started</dt><dd>{formatDate(run.startedAt)}</dd></div>
            <div><dt>Completed</dt><dd>{formatDate(run.completedAt)}</dd></div>
          </dl>
        </div>
        <div className="detail-panel">
          <h2>Result</h2>
          <dl>
            <div><dt>Received</dt><dd>{run.receivedCount}</dd></div>
            <div><dt>Created</dt><dd>{run.createdCount}</dd></div>
            <div><dt>Updated</dt><dd>{run.updatedCount}</dd></div>
            <div><dt>Unchanged</dt><dd>{run.unchangedCount}</dd></div>
            <div><dt>Skipped</dt><dd>{run.skippedCount}</dd></div>
            <div><dt>Revalidated</dt><dd>{run.revalidatedCount}</dd></div>
          </dl>
        </div>
      </section>

      <section className="detail-panel detail-section">
        <h2>Reconciliation window</h2>
        <dl>
          <div><dt>Cursor before</dt><dd>{formatDate(run.cursorBefore)}</dd></div>
          <div><dt>Query from</dt><dd>{formatDate(run.queryFrom)}</dd></div>
          <div><dt>Query to</dt><dd>{formatDate(run.queryTo)}</dd></div>
          <div><dt>Cursor after</dt><dd>{formatDate(run.cursorAfter)}</dd></div>
        </dl>
      </section>

      <section className="detail-panel detail-section">
        <h2>Diagnostics</h2>
        <dl>
          <div><dt>Correlation ID</dt><dd>{run.correlationId}</dd></div>
          <div><dt>Message ID</dt><dd>{run.messageId ?? "-"}</dd></div>
          <div><dt>Error code</dt><dd>{run.errorCode ?? "-"}</dd></div>
          <div><dt>Error summary</dt><dd>{run.errorSummary ?? "-"}</dd></div>
        </dl>
      </section>
    </main>
  );
}

function formatDate(value?: string | null) {
  return value ? new Date(value).toLocaleString() : "-";
}

function Status({ value }: { value: string }) {
  return <span className={`status-pill status-${value.toLowerCase()}`}>{value}</span>;
}
