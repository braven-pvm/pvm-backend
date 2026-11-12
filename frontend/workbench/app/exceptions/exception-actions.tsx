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
import {
  Columns,
  ConfirmDialog,
  FormActions,
  FormLayout,
  Notice,
  Panel,
  RadioGroup,
  SelectField,
  SubmitButton,
  TextField,
} from "../../src/ui";

const outcomes = [
  {
    value: "ConfirmedAccepted",
    label: "Shoprite accepted it",
    hint: "The submission becomes Submitted. The invoice is never sent again.",
  },
  {
    value: "ConfirmedNotAccepted",
    label: "Shoprite did not accept it",
    hint: "The submission is cancelled and the invoice goes back to review for a new attempt.",
  },
  {
    value: "StillUnknown",
    label: "Still not known",
    hint: "Nothing changes. The evidence is recorded and the exception waits.",
  },
];

const statuses = [
  { value: "Open", label: "Open" },
  { value: "InProgress", label: "In progress" },
  { value: "WaitingForAcumatica", label: "Waiting for Acumatica" },
  { value: "WaitingForShoprite", label: "Waiting for Shoprite" },
  { value: "Suppressed", label: "Suppressed" },
  { value: "Resolved", label: "Resolved" },
];

/** The fix for this kind of exception. Only one applies to each exception. */
export function ExceptionFix({ task }: { task: ExceptionTask }) {
  if (task.category === "AmbiguousSubmission" && task.entityType === "SubmissionOperation") {
    return (
      <Panel title="Record what Shoprite says" description="Check the Shoprite portal first. While the outcome is unknown, there is no retry.">
        <form action={resolveAmbiguousSubmissionAction}>
          <input type="hidden" name="submissionOperationId" value={task.entityId} />
          <ReturnTo task={task} />
          <FormLayout>
            <RadioGroup name="outcome" label="What Shoprite says" required defaultValue="StillUnknown" options={outcomes} />
            <TextField name="evidence" label="How you checked" hint="A portal reference, a ticket number or the person you spoke to" width="l" required />
            <TextField name="reason" label="Reason" hint="Why this outcome is correct" width="l" required />
            <FormActions>
              <SubmitButton pendingLabel="Recording…">Record the outcome</SubmitButton>
            </FormActions>
          </FormLayout>
        </form>
      </Panel>
    );
  }

  if (task.category === "RejectedSubmission" && task.invoiceCandidateId) {
    return (
      <Panel title="Allow a new attempt" description="The invoice is checked again first. A new attempt is refused while a check still fails.">
        <ConfirmDialog
          triggerLabel="Check again and allow a retry"
          triggerVariant="primary"
          title={`Allow a new attempt for ${task.invoiceNumber ?? "this invoice"}?`}
          confirmLabel="Allow a retry"
          pendingLabel="Checking…"
          action={retrySubmissionAction}
          fields={{ invoiceCandidateId: task.invoiceCandidateId, returnTo: `/exceptions/${task.id}` }}
        >
          <p>The invoice is checked again. If it passes, the next submission sends it to Shoprite.</p>
          <TextField name="reason" label="What was corrected" width="full" required />
        </ConfirmDialog>
      </Panel>
    );
  }

  if (task.category === "NeedsReview" && task.invoiceCandidateId) {
    return (
      <Panel title="Hold the invoice" description="A held invoice is never sent, by hand or by an automation, until someone releases it.">
        <form action={holdInvoiceAction}>
          <input type="hidden" name="invoiceCandidateId" value={task.invoiceCandidateId} />
          <ReturnTo task={task} />
          <FormLayout>
            <TextField name="reason" label="Reason" hint="Why the invoice must not be sent" width="l" required />
            <FormActions>
              <SubmitButton variant="secondary" pendingLabel="Holding…">Hold invoice</SubmitButton>
            </FormActions>
          </FormLayout>
        </form>
      </Panel>
    );
  }

  if (task.category === "HeldInvoice" && task.invoiceCandidateId) {
    return (
      <Panel title="Release the hold" description="The invoice is checked again, then follows the normal rules.">
        <form action={releaseInvoiceAction}>
          <input type="hidden" name="invoiceCandidateId" value={task.invoiceCandidateId} />
          <ReturnTo task={task} />
          <FormLayout>
            <TextField name="reason" label="Reason" hint="Why the hold can end" width="l" required />
            <FormActions>
              <SubmitButton pendingLabel="Releasing…">Release and check again</SubmitButton>
            </FormActions>
          </FormLayout>
        </form>
      </Panel>
    );
  }

  if (task.category === "DeadLetter") {
    return task.retryClassification === "safe-to-replay" ? (
      <Panel title="Replay the message" description="The message runs again as a new command.">
        <ConfirmDialog
          triggerLabel="Replay message"
          triggerVariant="primary"
          title="Replay this message?"
          confirmLabel="Replay message"
          pendingLabel="Replaying…"
          action={replayDeadLetterAction}
          fields={{ deliveryId: task.entityId, returnTo: `/exceptions/${task.id}` }}
        >
          <p>The message runs again as a new command. Replay only when the cause is fixed.</p>
          <TextField name="reason" label="Why a replay is safe now" width="full" required />
        </ConfirmDialog>
      </Panel>
    ) : (
      <Notice tone="info" title="This message cannot be replayed">
        {task.latestEvidence ?? "The message type does not allow a replay."} Close it with a reason in Status.
      </Notice>
    );
  }

  return null;
}

/** Assign, comment and status: the same for every exception. */
export function ExceptionWork({ task }: { task: ExceptionTask }) {
  return (
    <Columns>
      <Panel title="Owner">
        <form action={assignExceptionAction}>
          <input type="hidden" name="id" value={task.id} />
          <ReturnTo task={task} />
          <FormLayout>
            <TextField name="owner" label="Owner" hint="An email address. Leave empty to unassign" type="email" width="full" defaultValue={task.owner ?? ""} />
            <FormActions>
              <SubmitButton variant="secondary" pendingLabel="Saving…">Save owner</SubmitButton>
            </FormActions>
          </FormLayout>
        </form>
      </Panel>
      <Panel title="Comment">
        <form action={commentOnExceptionAction}>
          <input type="hidden" name="id" value={task.id} />
          <ReturnTo task={task} />
          <FormLayout>
            <TextField name="body" label="Comment" hint="What you found or did" width="full" required />
            <FormActions>
              <SubmitButton variant="secondary" pendingLabel="Adding…">Add comment</SubmitButton>
            </FormActions>
          </FormLayout>
        </form>
      </Panel>
      <Panel title="Status">
        <form action={setExceptionStatusAction}>
          <input type="hidden" name="id" value={task.id} />
          <ReturnTo task={task} />
          <FormLayout>
            <SelectField name="status" label="Status" width="full" required defaultValue={task.status} options={statuses} />
            <TextField name="reason" label="Reason" hint="Needed to resolve or suppress" width="full" required />
            <FormActions>
              <SubmitButton variant="secondary" pendingLabel="Saving…">Save status</SubmitButton>
            </FormActions>
          </FormLayout>
        </form>
      </Panel>
    </Columns>
  );
}

function ReturnTo({ task }: { task: ExceptionTask }) {
  return <input type="hidden" name="returnTo" value={`/exceptions/${task.id}`} />;
}
