import Link from "next/link";
import { RefreshCw } from "lucide-react";
import { enqueueIntegrationCommandAction } from "../../actions";
import { getIntegrationMessages, type IntegrationMessageView } from "../../../src/api/client";
import { requireWorkbenchUser } from "../../../src/auth/session";
import { formatDateTime } from "../../../src/formatters.mjs";
import { DataTable, EmptyState, FilterChips, ListPage, Notice, StatusBadge, SubmitButton, type Column } from "../../../src/ui";
import { EventsSections } from "../events-sections";

export const dynamic = "force-dynamic";
export const metadata = { title: "Queue messages" };

type MessagesPageProps = {
  searchParams: Promise<{ view?: string }>;
};

type Delivery = IntegrationMessageView["deliveries"][number];
type Outbox = IntegrationMessageView["outbox"][number];

const deliveryColumns: Column<Delivery>[] = [
  { key: "message", header: "Message", render: (row) => row.messageType },
  { key: "updated", header: "Updated", render: (row) => formatDateTime(row.updatedAt) },
  { key: "queue", header: "Queue", render: (row) => row.queueName, mono: true },
  { key: "deliveries", header: "Tries", render: (row) => row.deliveryCount, align: "end" },
  { key: "status", header: "Status", render: (row) => <StatusBadge status={row.status} /> },
  { key: "result", header: "Result", render: (row) => row.errorSummary ?? row.deadLetterReason ?? row.errorCode ?? "—", weak: true },
];

const outboxColumns: Column<Outbox>[] = [
  { key: "message", header: "Command", render: (row) => row.messageType },
  { key: "created", header: "Created", render: (row) => formatDateTime(row.createdAt) },
  { key: "queue", header: "Queue", render: (row) => row.queueName, mono: true },
  { key: "attempts", header: "Tries", render: (row) => row.publishAttempts, align: "end" },
  { key: "status", header: "Status", render: (row) => <StatusBadge status={row.status} /> },
  { key: "result", header: "Result", render: (row) => row.lastErrorSummary ?? row.lastErrorCode ?? "—", weak: true },
];

export default async function IntegrationMessagesPage({ searchParams }: MessagesPageProps) {
  const user = await requireWorkbenchUser("/admin/messages");
  const header = {
    eyebrow: "Automation · events",
    title: "Queue messages",
    description: "Commands the backoffice queued, and what the worker did with each one.",
  };

  if (!user.roles.includes("Admin")) {
    return (
      <ListPage
        header={header}
        sections={<EventsSections current="messages" />}
        notice={<Notice tone="info" title="Admin access needed">Queue records are for Admins only.</Notice>}
        table={null}
      />
    );
  }

  const { view: requestedView } = await searchParams;
  const showOutbox = requestedView === "outbox";
  const view = await getIntegrationMessages();
  const summary = view.summary;

  return (
    <ListPage
      header={{
        ...header,
        actions: (
          <form action={enqueueIntegrationCommandAction}>
            <input type="hidden" name="command" value="acumatica-discovery" />
            <SubmitButton variant="secondary" pendingLabel="Queueing…">
              <RefreshCw aria-hidden="true" />
              Read invoices from Acumatica now
            </SubmitButton>
          </form>
        ),
      }}
      sections={<EventsSections current="messages" />}
      notice={
        summary.deadLettered > 0 ? (
          <Notice tone="warning" title={summary.deadLettered === 1 ? "1 message stopped after repeated failures" : `${summary.deadLettered} messages stopped after repeated failures`}>
            <p>
              Work them in <Link href="/exceptions?tab=dead-letters">Exceptions, Dead letters</Link>.
            </p>
          </Notice>
        ) : null
      }
      filters={
        <FilterChips
          label="Choose the message list"
          chips={[
            { label: "Worker deliveries", count: summary.deliveryTotal, href: "/admin/messages", current: !showOutbox },
            { label: "Outbox", count: summary.outboxTotal, href: "/admin/messages?view=outbox", current: showOutbox },
          ]}
        />
      }
      table={
        showOutbox ? (
          <DataTable caption="Outbox" columns={outboxColumns} rows={view.outbox} rowKey={(row) => row.id} empty={<EmptyState title="No commands queued" />} />
        ) : (
          <DataTable caption="Worker deliveries" columns={deliveryColumns} rows={view.deliveries} rowKey={(row) => row.id} empty={<EmptyState title="No deliveries yet" />} />
        )
      }
    />
  );
}
