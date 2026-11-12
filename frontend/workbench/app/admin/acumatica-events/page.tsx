import { getAcumaticaPushNotificationEvents, type AcumaticaPushNotificationEventView } from "../../../src/api/client";
import { requireWorkbenchUser } from "../../../src/auth/session";
import { formatDateTime } from "../../../src/formatters.mjs";
import { DataTable, EmptyState, ListPage, Notice, StatTile, StatusBadge, TileGrid, type Column } from "../../../src/ui";
import { EventsSections } from "../events-sections";

export const dynamic = "force-dynamic";
export const metadata = { title: "Acumatica webhooks" };

type WebhookEvent = AcumaticaPushNotificationEventView["events"][number];

const columns: Column<WebhookEvent>[] = [
  { key: "transaction", header: "Transaction", render: (event) => event.transactionId, mono: true },
  { key: "received", header: "Received", render: (event) => formatDateTime(event.receivedAt) },
  { key: "query", header: "Acumatica query", render: (event) => event.queryName },
  { key: "rows", header: "Rows", render: (event) => `${event.insertedCount} added · ${event.deletedCount} removed`, align: "end" },
  { key: "queued", header: "Queued", render: (event) => event.enqueuedCount, align: "end" },
  {
    key: "duplicate",
    header: "Result",
    render: (event) =>
      event.duplicateCount > 0 ? <StatusBadge status="Skipped" label="Duplicate" /> : <StatusBadge status="Processed" />,
  },
];

export default async function AcumaticaWebhookEventsPage() {
  const user = await requireWorkbenchUser("/admin/acumatica-events");
  const header = {
    eyebrow: "Automation · events",
    title: "Acumatica webhooks",
    description: "Notifications that Acumatica sends when an invoice is finalised. Each one starts an invoice read.",
  };

  if (!user.roles.includes("Admin")) {
    return (
      <ListPage
        header={header}
        sections={<EventsSections current="webhooks" />}
        notice={<Notice tone="info" title="Admin access needed">Webhook records are for Admins only.</Notice>}
        table={null}
      />
    );
  }

  const view = await getAcumaticaPushNotificationEvents();
  const health = view.health;

  return (
    <ListPage
      header={{ ...header, actions: <StatusBadge status={health.status === "Waiting" ? "Late" : health.status} label={health.status === "NotConfigured" ? "Not configured" : undefined} /> }}
      sections={<EventsSections current="webhooks" />}
      attention={
        <TileGrid>
          <StatTile label="Events" value={String(health.eventCount)} detail="Since the webhook was set up" />
          <StatTile label="Duplicates" value={String(health.duplicateCount)} detail="Skipped, nothing read twice" />
          <StatTile label="Last received" value={health.lastReceivedAt ? formatDateTime(health.lastReceivedAt) : "Never"} />
          <StatTile
            label="Delay"
            value={health.lastEventLagSeconds === null || health.lastEventLagSeconds === undefined ? "—" : `${Math.round(health.lastEventLagSeconds)} s`}
            detail="From Acumatica to the backoffice"
          />
        </TileGrid>
      }
      table={
        <DataTable
          caption="Acumatica webhook events"
          columns={columns}
          rows={view.events}
          rowKey={(event) => event.id}
          empty={<EmptyState title="No webhook events yet" description="Events show here when Acumatica finalises an invoice for a Shoprite customer." />}
        />
      }
    />
  );
}
