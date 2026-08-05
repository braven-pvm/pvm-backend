import Link from "next/link";
import { getIntegrationRuns } from "../../src/api/client";
import { requireWorkbenchUser } from "../../src/auth/session";

export const dynamic = "force-dynamic";

export default async function IntegrationRunsPage() {
  await requireWorkbenchUser("/runs");
  const runs = await getIntegrationRuns();

  return (
    <main className="page-shell">
      <section className="page-heading">
        <div>
          <h1>Integration Runs</h1>
          <p>Scheduled and manual execution history across integration workflows.</p>
        </div>
      </section>

      <section className="table-panel" aria-label="Integration runs">
        <div className="table-toolbar"><h2>Runs</h2><span>Latest {runs.length}</span></div>
        {runs.length === 0 ? (
          <div className="empty-state"><strong>No integration runs recorded</strong></div>
        ) : (
          <table>
            <thead><tr><th>Created</th><th>Run</th><th>Trigger</th><th>Status</th><th>Attempts</th><th>Result</th><th></th></tr></thead>
            <tbody>{runs.map((run) => (
              <tr key={run.id}>
                <td data-label="Created">{new Date(run.createdAt).toLocaleString()}</td>
                <td data-label="Run">{run.runType}<span>{run.id}</span></td>
                <td data-label="Trigger">{run.trigger}<span>{run.initiatedBy}</span></td>
                <td data-label="Status"><Status value={run.status} /></td>
                <td data-label="Attempts">{run.attemptCount}</td>
                <td data-label="Result">{run.receivedCount} received<span>{run.revalidatedCount} candidates revalidated</span></td>
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
