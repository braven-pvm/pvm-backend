import Link from "next/link";
import { notFound } from "next/navigation";
import { getExceptions, type ExceptionTask } from "../../../src/api/client";
import { hasAnyRole, requireWorkbenchUser } from "../../../src/auth/session";
import { formatDateTime } from "../../../src/formatters.mjs";
import { EmptyState, KeyValueList, Notice, RecordPage, StatusBadge, Timeline } from "../../../src/ui";
import { ExceptionFix, ExceptionWork } from "../exception-actions";
import { categoryLabel, severityTone } from "../queues";

export const dynamic = "force-dynamic";
export const metadata = { title: "Exception" };

type ExceptionPageProps = {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ exceptionError?: string; exceptionStatus?: string }>;
};

/** The API lists exceptions by queue only, so the record comes from the open list, then the resolved list. */
async function loadTask(id: string): Promise<ExceptionTask> {
  for (const status of ["active", "Resolved"]) {
    const listing = await getExceptions(status);
    const task = listing.tasks.find((item) => item.id === id);
    if (task) {
      return task;
    }
  }
  notFound();
}

export default async function ExceptionPage({ params, searchParams }: ExceptionPageProps) {
  const { id } = await params;
  const query = await searchParams;
  const user = await requireWorkbenchUser(`/exceptions/${id}`);
  const isAdmin = hasAnyRole(user, ["Admin"]);
  const task = await loadTask(id);
  const resolved = task.status === "Resolved";

  return (
    <RecordPage
      back={{ href: "/exceptions", label: "Exceptions" }}
      header={{
        eyebrow: categoryLabel(task.category),
        title: task.summary,
        description: task.invoiceNumber ? `Invoice ${task.invoiceNumber} · first seen ${formatDateTime(task.firstSeenAt)}` : `First seen ${formatDateTime(task.firstSeenAt)}`,
        actions: (
          <>
            <StatusBadge status={task.severity} tone={severityTone(task.severity)} />
            {task.isOverdue ? <StatusBadge status="Overdue" /> : <StatusBadge status={task.status} />}
          </>
        ),
      }}
      notice={
        <>
          {query.exceptionError ? (
            <Notice tone="error" title="Action refused" live>
              {query.exceptionError}
            </Notice>
          ) : null}
          {query.exceptionStatus ? <Notice tone="success" title={query.exceptionStatus} live /> : null}
          {resolved ? (
            <Notice tone="success" title={`Resolved by ${task.resolvedBy ?? "someone"}`}>
              {task.resolutionReason}
            </Notice>
          ) : (
            <Notice tone="info" title={`Fix it in: ${task.fixLocation}`}>
              {task.latestEvidence}
            </Notice>
          )}
        </>
      }
      summary={
        <KeyValueList
          items={[
            { label: "Error code", value: task.errorCode, mono: true },
            { label: "Retry", value: task.retryClassification },
            { label: "Owner", value: task.owner },
            { label: "Seen", value: task.occurrenceCount === 1 ? "Once" : `${task.occurrenceCount} times` },
            { label: "Last seen", value: formatDateTime(task.lastSeenAt) },
            { label: "Due", value: task.dueAt ? formatDateTime(task.dueAt) : null },
            {
              label: "Invoice",
              value: task.invoiceCandidateId ? <Link href={`/shoprite/invoices/${task.invoiceCandidateId}`}>{task.invoiceNumber ?? "Open invoice"}</Link> : null,
            },
          ]}
        />
      }
      activity={
        task.comments.length > 0 ? (
          <Timeline
            label="Exception comments"
            entries={[...task.comments].reverse().map((comment) => ({
              id: comment.id,
              time: formatDateTime(comment.createdAt),
              dateTime: comment.createdAt,
              title: comment.body,
              detail: comment.actor,
            }))}
          />
        ) : (
          <EmptyState title="No comments yet" description="Comments from people who work this exception show here." />
        )
      }
    >
      {isAdmin && !resolved ? (
        <>
          <ExceptionFix task={task} />
          <ExceptionWork task={task} />
        </>
      ) : null}
      {!isAdmin ? (
        <Notice tone="info" title="Only an Admin can act on exceptions">
          Ask an Admin to assign, retry or close this exception.
        </Notice>
      ) : null}
    </RecordPage>
  );
}
