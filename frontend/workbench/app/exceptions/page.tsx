import Link from "next/link";
import { getExceptions, type ExceptionTask } from "../../src/api/client";
import { hasAnyRole, requireWorkbenchUser } from "../../src/auth/session";
import { ExceptionActions } from "./exception-actions";
import { DeadLetterBulkForm } from "./dead-letter-bulk-form";

export const dynamic = "force-dynamic";

type ExceptionsPageProps = {
  searchParams: Promise<{
    tab?: string;
    exceptionError?: string;
    exceptionStatus?: string;
  }>;
};

const tabs = [
  { key: "all", label: "All open", category: undefined, status: "active" },
  { key: "ambiguous", label: "Ambiguous", category: "AmbiguousSubmission", status: "active" },
  { key: "rejected", label: "Rejected", category: "RejectedSubmission", status: "active" },
  { key: "needs-review", label: "Needs review", category: "NeedsReview", status: "active" },
  { key: "dead-letters", label: "Dead letters", category: "DeadLetter", status: "active" },
  { key: "stuck", label: "Stuck", category: "StuckWork", status: "active" },
  { key: "held", label: "Held", category: "HeldInvoice", status: "active" },
  { key: "resolved", label: "Resolved", category: undefined, status: "Resolved" },
] as const;

export default async function ExceptionsPage({ searchParams }: ExceptionsPageProps) {
  const user = await requireWorkbenchUser("/exceptions");
  const isAdmin = hasAnyRole(user, ["Admin"]);
  const params = await searchParams;
  const activeTab = tabs.find((tab) => tab.key === params.tab) ?? tabs[0];
  const listing = await getExceptions(activeTab.status, activeTab.category);
  const summary = listing.summary;

  return (
    <main className="page-shell exceptions-page">
      <section className="page-heading">
        <div>
          <h1>Exceptions</h1>
          <p>Work every unresolved integration problem to a safe disposition.</p>
        </div>
        <div className="mode-summary">
          <span>{summary.overdue} overdue</span>
          <strong>{listing.tasks.length} shown</strong>
        </div>
      </section>

      {params.exceptionError ? (
        <div className="alert-banner alert-error" role="alert">
          <strong>Action refused</strong><span>{params.exceptionError}</span>
        </div>
      ) : null}
      {params.exceptionStatus ? (
        <div className="alert-banner alert-success" role="status">
          <strong>Exception updated</strong><span>{params.exceptionStatus}</span>
        </div>
      ) : null}

      <section className="metric-strip four-up" aria-label="Exception counts">
        <div><span>Ambiguous</span><strong>{summary.ambiguous}</strong></div>
        <div><span>Rejected</span><strong>{summary.rejected}</strong></div>
        <div><span>Dead letters</span><strong>{summary.deadLetters}</strong></div>
        <div><span>Needs review</span><strong>{summary.needsReview}</strong></div>
      </section>

      <nav className="tab-strip" aria-label="Exception queues">
        {tabs.map((tab) => (
          <Link
            key={tab.key}
            className={tab.key === activeTab.key ? "tab is-active" : "tab"}
            href={`/exceptions?tab=${tab.key}`}
          >
            {tab.label}
          </Link>
        ))}
      </nav>

      {isAdmin && activeTab.key === "dead-letters" && summary.deadLetters > 0 ? (
        <DeadLetterBulkForm openCount={summary.deadLetters} />
      ) : null}

      {listing.tasks.length === 0 ? (
        <p className="empty-state">No exception is open in this queue.</p>
      ) : (
        <ol className="exception-list">
          {listing.tasks.map((task) => (
            <li key={task.id} className={`exception-card severity-${task.severity.toLowerCase()}`}>
              <header>
                <div>
                  <span className="section-kicker">{task.category}</span>
                  <h2>{task.summary}</h2>
                </div>
                <div className="exception-badges">
                  <span className={`badge severity-${task.severity.toLowerCase()}`}>{task.severity}</span>
                  <span className="badge">{task.status}</span>
                  {task.isOverdue ? <span className="badge badge-warning">Overdue</span> : null}
                </div>
              </header>

              <dl className="exception-facts">
                <div><dt>Error code</dt><dd>{task.errorCode}</dd></div>
                <div><dt>Fix location</dt><dd>{task.fixLocation}</dd></div>
                <div><dt>Retry</dt><dd>{task.retryClassification}</dd></div>
                <div><dt>Owner</dt><dd>{task.owner ?? "Unassigned"}</dd></div>
                <div><dt>Seen</dt><dd>{task.occurrenceCount} time(s)</dd></div>
                <div><dt>Last seen</dt><dd>{new Date(task.lastSeenAt).toLocaleString("en-ZA")}</dd></div>
              </dl>

              {task.latestEvidence ? <p className="exception-evidence">{task.latestEvidence}</p> : null}

              {task.invoiceCandidateId ? (
                <p className="exception-link">
                  <Link href={`/invoices/${task.invoiceCandidateId}`}>
                    Open invoice {task.invoiceNumber ?? task.invoiceCandidateId}
                  </Link>
                </p>
              ) : null}

              {task.comments.length > 0 ? (
                <ul className="exception-comments">
                  {task.comments.map((comment) => (
                    <li key={comment.id}>
                      <strong>{comment.actor}</strong>
                      <span>{comment.body}</span>
                      <time dateTime={comment.createdAt}>
                        {new Date(comment.createdAt).toLocaleString("en-ZA")}
                      </time>
                    </li>
                  ))}
                </ul>
              ) : null}

              {task.resolutionReason ? (
                <p className="exception-resolution">
                  Resolved by {task.resolvedBy}: {task.resolutionReason}
                </p>
              ) : null}

              {isAdmin ? <ExceptionActions task={task as ExceptionTask} /> : null}
            </li>
          ))}
        </ol>
      )}

      {!isAdmin ? <p className="read-only-note">Exception actions are available to Admin only.</p> : null}
    </main>
  );
}
