import { getAcumaticaPushNotificationEvents } from "../../../src/api/client";
import { requireWorkbenchUser } from "../../../src/auth/session";

export const dynamic = "force-dynamic";

export default async function AcumaticaWebhookEventsPage() {
  const user = await requireWorkbenchUser("/admin/acumatica-events");
  if (!user.roles.includes("Admin")) {
    return (
      <main className="page-shell">
        <section className="empty-state">
          <strong>Admin access required</strong>
          <p>Acumatica webhook delivery records are restricted to administrators.</p>
        </section>
      </main>
    );
  }

  const view = await getAcumaticaPushNotificationEvents();
  const health = view.health;

  return (
    <main className="page-shell">
      <section className="page-heading">
        <div>
          <h1>Acumatica Webhook Events</h1>
          <p>Inspect authenticated notification receipt, deduplication, and discovery handoff.</p>
        </div>
        <span className={`status-pill status-${health.status.toLowerCase()}`}>
          {health.status}
        </span>
      </section>

      <section className="metric-strip four-up" aria-label="Webhook summary">
        <div><span>Events</span><strong>{health.eventCount}</strong></div>
        <div><span>Duplicates</span><strong>{health.duplicateCount}</strong></div>
        <div>
          <span>Last received</span>
          <strong className="metric-date">
            {health.lastReceivedAt ? new Date(health.lastReceivedAt).toLocaleString() : "Never"}
          </strong>
        </div>
        <div>
          <span>Latest lag</span>
          <strong className="metric-text">
            {health.lastEventLagSeconds === null || health.lastEventLagSeconds === undefined
              ? "-"
              : `${Math.round(health.lastEventLagSeconds)}s`}
          </strong>
        </div>
      </section>

      <section className="table-panel" aria-label="Acumatica webhook event inbox">
        <div className="table-toolbar">
          <h2>Event inbox</h2>
          <span>Latest {view.events.length} of {health.eventCount}</span>
        </div>
        {view.events.length === 0 ? (
          <div className="empty-state">
            <strong>No webhook events received</strong>
            <p>The endpoint is ready for an allowed Acumatica notification source.</p>
          </div>
        ) : (
          <table>
            <thead>
              <tr><th>Received</th><th>Source</th><th>Transaction</th><th>Rows</th><th>Queued</th><th>Duplicates</th></tr>
            </thead>
            <tbody>{view.events.map((event) => (
              <tr key={event.id}>
                <td data-label="Received">{new Date(event.receivedAt).toLocaleString()}<span>Last {new Date(event.lastReceivedAt).toLocaleString()}</span></td>
                <td data-label="Source">{event.companyId}<span>{event.queryName} / {event.sourceEnvironment}</span></td>
                <td data-label="Transaction">{event.transactionId}<span>{event.payloadHash}</span></td>
                <td data-label="Rows">{event.insertedCount} inserted / {event.deletedCount} deleted</td>
                <td data-label="Queued">{event.enqueuedCount}</td>
                <td data-label="Duplicates">{event.duplicateCount}</td>
              </tr>
            ))}</tbody>
          </table>
        )}
      </section>
    </main>
  );
}
