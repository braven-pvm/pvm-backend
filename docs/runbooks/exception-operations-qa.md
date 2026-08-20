# Exception Operations QA Runbook

Purpose: verify Slice 8 in QA. Slice 8 gives an Admin one place to work every
unresolved integration problem to a safe disposition.

Automatic submission stays `Disabled` during this runbook.

## Scope

The exception console covers six queues:

- Ambiguous submissions.
- Rejected submissions.
- Invoices that need review.
- Dead-lettered messages.
- Stuck work.
- Held invoices.

## Before You Start

1. Confirm that the automation policy is `Disabled` and the emergency stop is clear.
2. Confirm that the QA deployment carries the `AddExceptionTasks` migration.
3. Open the console at `/exceptions`.

## Test 1: Deduplication

1. Open `/exceptions` twice.
2. Confirm that each condition produces exactly one task.
3. Confirm that the occurrence count rises only when the evidence changes.

Pass: no duplicate task appears for the same condition.

## Test 2: Needs-review queue and hold

1. Open the **Needs review** tab.
2. Select an invoice that fails validation.
3. Enter a reason, then click **Hold**.
4. Open that invoice and click **Submit**.

Pass: the application refuses the submission with a hold message. The invoice
status is `Suspended`.

## Test 3: Release

1. Open the **Held** tab.
2. Enter a reason, then click **Release and revalidate**.

Pass: the invoice returns to `Ready` or `NeedsReview` according to validation.
The audit trail contains `invoice-hold-applied` and `invoice-hold-released`.

## Test 4: Ambiguous resolution

Caution: never record an outcome that you did not confirm with Shoprite.

1. Open the **Ambiguous** tab.
2. Read the invoice, the request hash, and the response evidence.
3. Check the submission with Shoprite.
4. Select the outcome, record how you checked, and give a reason.

Pass:

- `Confirmed accepted` sets the operation to `Submitted`. The invoice is never sent again.
- `Confirmed not accepted` cancels the operation and returns the invoice to review.
- `Still unknown` changes no state. The task moves to `WaitingForShoprite`.

The console offers no generic retry while the outcome is unknown.

## Test 5: Safe retry

1. Open the **Rejected** tab.
2. Enter a reason, then click **Revalidate and allow a retry**.

Pass: a retry is authorized only when revalidation returns `Ready`. Otherwise
the console refuses the retry and writes a `submission-retry-refused` audit
event. A retry is always refused while the outcome is ambiguous, and it is
always refused after Shoprite accepted the invoice.

## Test 6: Dead-letter replay

1. Open the **Dead letters** tab.
2. Read the replay assessment on each task.

Pass:

- A refresh or discovery message shows `safe-to-replay`.
- A submission message shows `manual-resolution-required` when any operation for
  that invoice is `Sending`, `Submitted`, or `Ambiguous`.
- A replay creates a new message identity. The original message identity becomes
  the causation identity, so the consumer cannot deduplicate the replay away.

## Test 7: Close historical dead letters

Use this when an incident is already fixed and the broker no longer holds the
messages. Resolution never replays a message.

1. Open the **Dead letters** tab.
2. Select the queue, or leave `Every queue`.
3. Set the age in days. A message newer than that age is kept.
4. Give the incident reference as the reason, then confirm.

Pass: the matching delivery rows become `DeadLetterResolved`, their tasks close
with your reason, and one `dead-letters-resolved` audit event records the count,
the queue, the age limit, and the oldest and newest message times. The next
synchronisation does not derive those tasks again.

## Test 8: Audit

Confirm that the audit trail records these actions with actor and reason:

- `manual-submission-refused`
- `invoice-hold-applied`
- `invoice-hold-released`
- `submission-retry-authorized`
- `submission-retry-refused`
- `ambiguous-submission-resolved`
- `ambiguous-submission-evidence-recorded`
- `dead-letter-replayed`
- `dead-letters-resolved`
- `exception-task-assigned`
- `exception-task-status-changed`

## Database Checks

Use the QA connection string from Key Vault `kv-pvm-intg-qa`, secret
`connectionstrings--pvm`.

```sql
select "Category", "Status", count(*) from exception_tasks group by 1, 2 order by 1;
```

```sql
select "Action", count(*) from audit_events where "CreatedAt" > now() - interval '1 day' group by 1;
```
