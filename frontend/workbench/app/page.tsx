import Link from "next/link";
import { getOperationsSummary } from "../src/api/client";
import { requireWorkbenchUser } from "../src/auth/session";

export const dynamic = "force-dynamic";

export default async function HomePage() {
  await requireWorkbenchUser("/");
  const view = await getOperationsSummary();
  const freshness = view.purchaseOrderFreshness;
  const reconciliation = view.acumaticaReconciliationFreshness;
  const webhook = view.acumaticaPushNotificationHealth;

  return (
    <main className="page-shell">
      <section className="page-heading">
        <div>
          <h1>Control Room</h1>
          <p>Current integration health, workload, and recent execution history.</p>
        </div>
        <div className="mode-summary" aria-label="Environment and automation mode">
          <span>{view.environmentName}</span>
          <strong>Automation {view.automationMode}</strong>
        </div>
      </section>

      <section className="metric-strip four-up" aria-label="Operational summary">
        <div>
          <span>PO data</span>
          <strong className="metric-date">
            {freshness.status} - {freshness.lastSuccessfulRefreshAt
              ? new Date(freshness.lastSuccessfulRefreshAt).toLocaleString()
              : "Never"}
          </strong>
        </div>
        <div>
          <span>Invoice sync</span>
          <strong className="metric-date">
            {reconciliation.status} - {reconciliation.lastSuccessfulReconciliationAt
              ? new Date(reconciliation.lastSuccessfulReconciliationAt).toLocaleString()
              : "Never"}
          </strong>
        </div>
        <div>
          <span>Acumatica webhook</span>
          <strong className="metric-date">
            {webhook.status} - {webhook.lastReceivedAt
              ? new Date(webhook.lastReceivedAt).toLocaleString()
              : "No events"}
          </strong>
        </div>
        <div><span>Active runs</span><strong>{view.summary.activeRuns}</strong></div>
      </section>

      <section className="metric-strip four-up" aria-label="Workload summary">
        <div><span>Candidate invoices</span><strong>{view.summary.candidateInvoices}</strong></div>
        <div><span>Needs review</span><strong>{view.summary.needsReview}</strong></div>
        <div><span>Pending messages</span><strong>{view.summary.pendingMessages}</strong></div>
        <div><span>Dead letters</span><strong>{view.summary.deadLetters}</strong></div>
      </section>

      <p className="compact-stats">
        <span><strong>{view.summary.failedRuns}</strong> failed runs in 24 hours</span>
        <span><strong>{webhook.eventCount}</strong> webhook events</span>
        <span><strong>{webhook.duplicateCount}</strong> duplicate deliveries</span>
        <span><strong>{webhook.lastEventLagSeconds === null || webhook.lastEventLagSeconds === undefined
          ? "-"
          : `${Math.round(webhook.lastEventLagSeconds)}s`}</strong> latest webhook lag</span>
      </p>

      <section className="table-panel" aria-label="Latest integration runs">
        <div className="table-toolbar">
          <h2>Latest runs</h2>
          <Link href="/runs">View all</Link>
        </div>
        {view.latestRuns.length === 0 ? (
          <div className="empty-state"><strong>No integration runs recorded</strong></div>
        ) : (
          <table>
            <thead><tr><th>Created</th><th>Run</th><th>Trigger</th><th>Status</th><th>Result</th><th></th></tr></thead>
            <tbody>{view.latestRuns.map((run) => (
              <tr key={run.id}>
                <td data-label="Created">{new Date(run.createdAt).toLocaleString()}</td>
                <td data-label="Run">{run.runType}<span>{run.id}</span></td>
                <td data-label="Trigger">{run.trigger}</td>
                <td data-label="Status"><Status value={run.status} /></td>
                <td data-label="Result">{run.receivedCount} received / {run.failedCount} failed</td>
                <td className="table-action" data-label="Action"><Link href={`/runs/${run.id}`}>Open</Link></td>
              </tr>
            ))}</tbody>
          </table>
        )}
      </section>
    </main>
  );
}

function Status({ value }: { value: string }) {
  return <span className={`status-pill status-${value.toLowerCase()}`}>{value}</span>;
}
