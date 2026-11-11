import { resolveDeadLettersAction } from "../actions";
import { getExceptions, type ExceptionTask } from "../../src/api/client";
import { hasAnyRole, requireWorkbenchUser } from "../../src/auth/session";
import { formatDateTime } from "../../src/formatters.mjs";
import {
  ConfirmDialog,
  DataTable,
  EmptyState,
  FilterChips,
  ListPage,
  Notice,
  SelectField,
  StatusBadge,
  TextField,
  type Column,
} from "../../src/ui";
import { categoryLabel, queues, severityTone } from "./queues";

export const dynamic = "force-dynamic";
export const metadata = { title: "Exceptions" };

type ExceptionsPageProps = {
  searchParams: Promise<{ tab?: string; exceptionError?: string; exceptionStatus?: string }>;
};

const deadLetterQueues = [
  { value: "shoprite-po-refresh", label: "shoprite-po-refresh" },
  { value: "acumatica-invoice-discovery", label: "acumatica-invoice-discovery" },
  { value: "shoprite-invoice-submit", label: "shoprite-invoice-submit" },
];

const columns: Column<ExceptionTask>[] = [
  { key: "summary", header: "What happened", render: (task) => task.summary },
  {
    key: "severity",
    header: "Severity",
    render: (task) => <StatusBadge status={task.severity} tone={severityTone(task.severity)} />,
  },
  { key: "category", header: "Queue", render: (task) => categoryLabel(task.category), weak: true },
  { key: "invoice", header: "Invoice", render: (task) => task.invoiceNumber ?? "—", mono: true },
  { key: "owner", header: "Owner", render: (task) => task.owner ?? "Nobody" },
  { key: "seen", header: "Last seen", render: (task) => formatDateTime(task.lastSeenAt), weak: true },
  {
    key: "status",
    header: "Status",
    render: (task) => (task.isOverdue ? <StatusBadge status="Overdue" /> : <StatusBadge status={task.status} />),
  },
];

export default async function ExceptionsPage({ searchParams }: ExceptionsPageProps) {
  const user = await requireWorkbenchUser("/exceptions");
  const isAdmin = hasAnyRole(user, ["Admin"]);
  const params = await searchParams;
  const queue = queues.find((item) => item.key === params.tab) ?? queues[0];
  const listing = await getExceptions(queue.status, queue.category);
  const summary = listing.summary;

  return (
    <ListPage
      header={{
        eyebrow: "Work queue",
        title: "Exceptions",
        description: `Problems an automation could not finish on its own. ${summary.overdue} overdue.`,
        actions:
          isAdmin && queue.key === "dead-letters" && summary.deadLetters > 0 ? (
            <ConfirmDialog
              triggerLabel="Close old dead letters"
              triggerVariant="secondary"
              title="Close old dead letters?"
              confirmLabel="Close dead letters"
              pendingLabel="Closing…"
              action={resolveDeadLettersAction}
            >
              <p>
                Use this when the incident is fixed and the messages are no longer needed. Closing never replays a message.
                The reason goes in the audit log. {summary.deadLetters} dead letters are open.
              </p>
              <SelectField
                name="queueName"
                label="Queue"
                width="full"
                placeholder="Every queue"
                showOptional={false}
                options={deadLetterQueues}
              />
              <TextField name="olderThanDays" label="Older than (days)" type="number" min={0} defaultValue="1" width="s" required />
              <TextField name="reason" label="Reason" hint="The incident reference, and why a replay is not needed" width="full" required />
            </ConfirmDialog>
          ) : null,
      }}
      notice={
        <>
          {params.exceptionError ? (
            <Notice tone="error" title="Action refused" live>
              {params.exceptionError}
            </Notice>
          ) : null}
          {params.exceptionStatus ? <Notice tone="success" title={params.exceptionStatus} live /> : null}
          {!isAdmin ? (
            <Notice tone="info" title="Only an Admin can act on exceptions">
              You can see every exception. Ask an Admin to assign, retry or close one.
            </Notice>
          ) : null}
        </>
      }
      filters={
        <FilterChips
          label="Exception queues"
          chips={queues.map((item) => ({
            label: item.label,
            count: item.count(summary),
            href: item.key === "all" ? "/exceptions" : `/exceptions?tab=${item.key}`,
            current: item.key === queue.key,
          }))}
        />
      }
      table={
        <DataTable
          caption={`${queue.label} exceptions`}
          columns={columns}
          rows={listing.tasks}
          rowKey={(task) => task.id}
          rowHref={(task) => `/exceptions/${task.id}`}
          empty={
            <EmptyState
              title={queue.key === "resolved" ? "Nothing resolved yet" : "Nothing is open in this queue"}
              description={queue.key === "resolved" ? undefined : "Exceptions appear here when an automation cannot finish on its own."}
            />
          }
        />
      }
    />
  );
}
