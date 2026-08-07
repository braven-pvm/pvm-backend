import { enqueueIntegrationCommandAction } from "../../actions";
import { getIntegrationMessages } from "../../../src/api/client";
import { requireWorkbenchUser } from "../../../src/auth/session";

export const dynamic = "force-dynamic";

export default async function IntegrationMessagesPage() {
  const user = await requireWorkbenchUser("/admin/messages");
  if (!user.roles.includes("Admin")) {
    return (
      <main className="page-shell">
        <section className="empty-state">
          <strong>Admin access required</strong>
          <p>Integration command and delivery records are restricted to administrators.</p>
        </section>
      </main>
    );
  }

  const view = await getIntegrationMessages();

  return (
    <main className="page-shell">
      <section className="page-heading">
        <div>
          <h1>Integration Messages</h1>
          <p>Inspect durable commands, worker delivery, retries, and dead letters.</p>
        </div>
        <div className="command-actions">
          <form action={enqueueIntegrationCommandAction}>
            <input type="hidden" name="command" value="shoprite-po-refresh" />
            <button className="button secondary" type="submit">Queue PO refresh</button>
          </form>
          <form action={enqueueIntegrationCommandAction}>
            <input type="hidden" name="command" value="acumatica-discovery" />
            <button className="button" type="submit">Queue invoice reconciliation</button>
          </form>
        </div>
      </section>

      <section className="metric-strip four-up" aria-label="Message summary">
        <div><span>Pending publish</span><strong>{view.summary.pending}</strong></div>
        <div><span>Published</span><strong>{view.summary.published}</strong></div>
        <div><span>Retrying</span><strong>{view.summary.retrying}</strong></div>
        <div><span>Dead letters</span><strong>{view.summary.deadLettered}</strong></div>
      </section>

      <section className="table-panel" aria-label="Outbox messages">
        <div className="table-toolbar">
          <h2>Outbox</h2><span>Latest {view.outbox.length} of {view.summary.outboxTotal}</span>
        </div>
        {view.outbox.length === 0 ? <Empty label="No commands queued" /> : (
          <table>
            <thead><tr><th>Created</th><th>Command</th><th>Queue</th><th>Status</th><th>Attempts</th><th>Result</th></tr></thead>
            <tbody>{view.outbox.map((message) => (
              <tr key={message.id}>
                <td data-label="Created">{new Date(message.createdAt).toLocaleString()}</td>
                <td data-label="Command">{message.messageType}<span>{message.correlationId}</span></td>
                <td data-label="Queue">{message.queueName}</td>
                <td data-label="Status"><Status value={message.status} /></td>
                <td data-label="Attempts">{message.publishAttempts}</td>
                <td data-label="Result">{message.lastErrorCode ?? "-"}<span>{message.lastErrorSummary}</span></td>
              </tr>
            ))}</tbody>
          </table>
        )}
      </section>

      <section className="table-panel detail-section" aria-label="Message deliveries">
        <div className="table-toolbar">
          <h2>Worker deliveries</h2><span>Latest {view.deliveries.length} of {view.summary.deliveryTotal}</span>
        </div>
        {view.deliveries.length === 0 ? <Empty label="No worker deliveries recorded" /> : (
          <table>
            <thead><tr><th>Updated</th><th>Message</th><th>Queue</th><th>Status</th><th>Deliveries</th><th>Result</th></tr></thead>
            <tbody>{view.deliveries.map((delivery) => (
              <tr key={delivery.id}>
                <td data-label="Updated">{new Date(delivery.updatedAt).toLocaleString()}</td>
                <td data-label="Message">{delivery.messageType}<span>{delivery.messageId}</span></td>
                <td data-label="Queue">{delivery.queueName}</td>
                <td data-label="Status"><Status value={delivery.status} /></td>
                <td data-label="Deliveries">{delivery.deliveryCount}</td>
                <td data-label="Result">{delivery.deadLetterReason ?? delivery.errorCode ?? "-"}<span>{delivery.errorSummary}</span></td>
              </tr>
            ))}</tbody>
          </table>
        )}
      </section>
    </main>
  );
}

function Empty({ label }: { label: string }) {
  return <div className="empty-state"><strong>{label}</strong></div>;
}

function Status({ value }: { value: string }) {
  return <span className={`status-pill status-${value.toLowerCase()}`}>{value}</span>;
}
