import Link from "next/link";
import { RefreshCw } from "lucide-react";
import { enqueueIntegrationCommandAction } from "../actions";
import { getAcumaticaPushNotificationEvents, getIntegrationMessages } from "../../src/api/client";
import { hasAnyRole, requireWorkbenchUser } from "../../src/auth/session";
import { formatDateTime } from "../../src/formatters.mjs";
import {
  ButtonLink,
  DataTable,
  EmptyState,
  FilterChips,
  ListPage,
  Notice,
  StatTile,
  StatusBadge,
  SubmitButton,
  TileGrid,
  type Column,
} from "../../src/ui";
import { buildStream, sourceLabel, type EventSource, type StreamEvent } from "./stream";

export const dynamic = "force-dynamic";
export const metadata = { title: "Events" };

type EventsPageProps = {
  searchParams: Promise<{ source?: string; queued?: string }>;
};

const filters: Array<{ value?: EventSource | "dead"; label: string }> = [
  { value: undefined, label: "All sources" },
  { value: "webhook", label: "Acumatica webhooks" },
  { value: "delivery", label: "Worker deliveries" },
  { value: "outbox", label: "Queued commands" },
  { value: "dead", label: "Dead lettered" },
];

const columns: Column<StreamEvent>[] = [
  { key: "name", header: "Event", render: (event) => event.name },
  { key: "at", header: "When", render: (event) => formatDateTime(event.at) },
  { key: "source", header: "Source", render: (event) => sourceLabel(event.source), weak: true },
  { key: "detail", header: "Detail", render: (event) => event.detail || "—", weak: true },
  {
    key: "status",
    header: "Status",
    render: (event) => (event.status === "Duplicate" ? <StatusBadge status="Skipped" label="Duplicate" /> : <StatusBadge status={event.status} />),
  },
];

export default async function EventsPage({ searchParams }: EventsPageProps) {
  const user = await requireWorkbenchUser("/events");
  const header = {
    eyebrow: "Automation",
    title: "Events",
    description: "What arrived from Acumatica, and what moved through the queues. Mostly for finding why a run failed.",
  };

  if (!hasAnyRole(user, ["Admin"])) {
    return <ListPage header={header} notice={<Notice tone="info" title="Admin access needed">Event records are for Admins only.</Notice>} table={null} />;
  }

  const params = await searchParams;
  const [webhooks, messages] = await Promise.all([getAcumaticaPushNotificationEvents(), getIntegrationMessages()]);
  const stream = buildStream(webhooks, messages);
  const matches = (event: StreamEvent, value?: EventSource | "dead") =>
    !value || (value === "dead" ? event.status === "DeadLettered" : event.source === value);
  const filter = filters.find((item) => item.value === params.source)?.value;
  const rows = stream.filter((event) => matches(event, filter));
  const health = webhooks.health;

  return (
    <ListPage
      header={{
        ...header,
        actions: (
          <form action={enqueueIntegrationCommandAction}>
            <input type="hidden" name="command" value="acumatica-discovery" />
            <input type="hidden" name="returnTo" value="/events" />
            <SubmitButton variant="secondary" pendingLabel="Queueing…">
              <RefreshCw aria-hidden="true" />
              Read invoices from Acumatica now
            </SubmitButton>
          </form>
        ),
      }}
      notice={
        <>
          {params.queued ? <Notice tone="success" title="Invoice read queued" live>The worker starts it within a minute.</Notice> : null}
          {messages.summary.deadLettered > 0 ? (
            <Notice tone="warning" title={messages.summary.deadLettered === 1 ? "1 message stopped after repeated failures" : `${messages.summary.deadLettered} messages stopped after repeated failures`}>
              <p>
                Work them in <Link href="/exceptions?tab=dead-letters">Exceptions, Dead letters</Link>.
              </p>
            </Notice>
          ) : null}
        </>
      }
      attention={
        <TileGrid>
          <StatTile
            label="Acumatica webhook"
            value={health.status === "NotConfigured" ? "Not configured" : health.status === "Waiting" ? "Waiting" : "Healthy"}
            detail={health.lastReceivedAt ? `Last event ${formatDateTime(health.lastReceivedAt)}` : "No events yet"}
            tone={health.status === "Healthy" ? "default" : "warning"}
          />
          <StatTile label="Webhook events" value={String(health.eventCount)} detail={`${health.duplicateCount} duplicates skipped`} />
          <StatTile label="Messages waiting" value={String(messages.summary.pending)} detail={`${messages.summary.retrying} retrying`} />
          <StatTile label="Dead letters" value={String(messages.summary.deadLettered)} tone={messages.summary.deadLettered > 0 ? "error" : "default"} />
        </TileGrid>
      }
      filters={
        <FilterChips
          label="Filter events by source"
          chips={filters.map((item) => ({
            label: item.label,
            count: stream.filter((event) => matches(event, item.value)).length,
            href: item.value ? `/events?source=${item.value}` : "/events",
            current: item.value === filter,
          }))}
        />
      }
      table={
        <DataTable
          caption="Events"
          columns={columns}
          rows={rows}
          rowKey={(event) => event.id}
          empty={
            stream.length === 0 ? (
              <EmptyState title="No events yet" />
            ) : (
              <EmptyState title="No events from this source" action={<ButtonLink href="/events">Show all events</ButtonLink>} />
            )
          }
        />
      }
    />
  );
}
