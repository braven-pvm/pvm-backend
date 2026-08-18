"use client";

import {
  assignExceptionAction,
  commentOnExceptionAction,
  holdInvoiceAction,
  releaseInvoiceAction,
  replayDeadLetterAction,
  resolveAmbiguousSubmissionAction,
  retrySubmissionAction,
  setExceptionStatusAction,
} from "../actions";
import type { ExceptionTask } from "../../src/api/client";

const outcomes = [
  {
    value: "ConfirmedAccepted",
    label: "Confirmed accepted",
    consequence: "The submission becomes Submitted. The invoice is never sent again.",
  },
  {
    value: "ConfirmedNotAccepted",
    label: "Confirmed not accepted",
    consequence: "The operation is cancelled and the invoice returns to review for a new attempt.",
  },
  {
    value: "StillUnknown",
    label: "Still unknown",
    consequence: "Nothing changes. The evidence is recorded and the task waits for Shoprite.",
  },
];

export function ExceptionActions({ task }: { task: ExceptionTask }) {
  const isSubmissionOperation = task.entityType === "SubmissionOperation";
  const canReplay = task.retryClassification === "safe-to-replay";

  return (
    <div className="exception-actions">
      {task.category === "AmbiguousSubmission" && isSubmissionOperation ? (
        <form action={resolveAmbiguousSubmissionAction} className="exception-form">
          <input name="submissionOperationId" type="hidden" value={task.entityId} />
          <fieldset>
            <legend>Resolve the ambiguous outcome</legend>
            <p className="field-note">
              Check Shoprite first. Record what you checked. There is no generic retry while the
              outcome is unknown.
            </p>
            <label>
              <span>Outcome *</span>
              <select name="outcome" required defaultValue="StillUnknown">
                {outcomes.map((outcome) => (
                  <option key={outcome.value} value={outcome.value}>
                    {outcome.label}
                  </option>
                ))}
              </select>
            </label>
            <ul className="consequence-list">
              {outcomes.map((outcome) => (
                <li key={outcome.value}>
                  <strong>{outcome.label}:</strong> {outcome.consequence}
                </li>
              ))}
            </ul>
            <label>
              <span>How Shoprite was checked *</span>
              <input name="evidence" placeholder="Portal reference, ticket number or contact" required />
            </label>
            <label>
              <span>Reason *</span>
              <input name="reason" placeholder="Why this outcome is correct" required />
            </label>
            <button className="button primary" type="submit">Record the outcome</button>
          </fieldset>
        </form>
      ) : null}

      {task.category === "RejectedSubmission" && task.invoiceCandidateId ? (
        <form
          action={retrySubmissionAction}
          className="exception-form"
          onSubmit={(event) => {
            if (!window.confirm("Revalidate this invoice and authorize a new submission attempt?")) {
              event.preventDefault();
            }
          }}
        >
          <input name="invoiceCandidateId" type="hidden" value={task.invoiceCandidateId} />
          <fieldset>
            <legend>Authorize a retry</legend>
            <p className="field-note">
              The invoice is revalidated first. A retry is refused while validation still fails.
            </p>
            <label>
              <span>Reason *</span>
              <input name="reason" placeholder="What was corrected" required />
            </label>
            <button className="button primary" type="submit">Revalidate and allow a retry</button>
          </fieldset>
        </form>
      ) : null}

      {task.category === "NeedsReview" && task.invoiceCandidateId ? (
        <form action={holdInvoiceAction} className="exception-form">
          <input name="invoiceCandidateId" type="hidden" value={task.invoiceCandidateId} />
          <fieldset>
            <legend>Hold this invoice</legend>
            <label>
              <span>Reason *</span>
              <input name="reason" placeholder="Why submission must not proceed" required />
            </label>
            <button className="button secondary" type="submit">Hold</button>
          </fieldset>
        </form>
      ) : null}

      {task.category === "HeldInvoice" && task.invoiceCandidateId ? (
        <form action={releaseInvoiceAction} className="exception-form">
          <input name="invoiceCandidateId" type="hidden" value={task.invoiceCandidateId} />
          <fieldset>
            <legend>Release the hold</legend>
            <label>
              <span>Reason *</span>
              <input name="reason" placeholder="Why the hold can end" required />
            </label>
            <button className="button primary" type="submit">Release and revalidate</button>
          </fieldset>
        </form>
      ) : null}

      {task.category === "DeadLetter" ? (
        canReplay ? (
          <form
            action={replayDeadLetterAction}
            className="exception-form"
            onSubmit={(event) => {
              if (!window.confirm("Replay this message as a new command?")) {
                event.preventDefault();
              }
            }}
          >
            <input name="deliveryId" type="hidden" value={task.entityId} />
            <fieldset>
              <legend>Replay the message</legend>
              <label>
                <span>Reason *</span>
                <input name="reason" placeholder="Why replay is safe now" required />
              </label>
              <button className="button primary" type="submit">Replay</button>
            </fieldset>
          </form>
        ) : (
          <p className="field-note replay-blocked">
            Replay is unavailable for this message. {task.latestEvidence}
          </p>
        )
      ) : null}

      <form action={assignExceptionAction} className="exception-form inline-form">
        <input name="id" type="hidden" value={task.id} />
        <label>
          <span>Owner</span>
          <input name="owner" defaultValue={task.owner ?? ""} placeholder="name@pvm.co.za" />
        </label>
        <button className="button secondary" type="submit">Assign</button>
      </form>

      <form action={commentOnExceptionAction} className="exception-form inline-form">
        <input name="id" type="hidden" value={task.id} />
        <label>
          <span>Comment</span>
          <input name="body" placeholder="What you found or did" required />
        </label>
        <button className="button secondary" type="submit">Add comment</button>
      </form>

      <form action={setExceptionStatusAction} className="exception-form inline-form">
        <input name="id" type="hidden" value={task.id} />
        <label>
          <span>Status</span>
          <select name="status" defaultValue={task.status}>
            <option value="Open">Open</option>
            <option value="InProgress">In progress</option>
            <option value="WaitingForAcumatica">Waiting for Acumatica</option>
            <option value="WaitingForShoprite">Waiting for Shoprite</option>
            <option value="Suppressed">Suppressed</option>
            <option value="Resolved">Resolved</option>
          </select>
        </label>
        <label>
          <span>Reason</span>
          <input name="reason" placeholder="Required to resolve or suppress" required />
        </label>
        <button className="button secondary" type="submit">Save status</button>
      </form>
    </div>
  );
}
