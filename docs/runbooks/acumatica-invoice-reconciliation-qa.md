# Acumatica Invoice Reconciliation QA

Use this runbook after the Slice 5 deployment. Keep `Automation__Mode=Disabled`
throughout verification. These jobs discover and prepare candidates; they must
not submit invoices to Shoprite.

## Expected Resources

- Incremental job: `job-pvm-invoice-reconcile-qa`
- Daily lookback job: `job-pvm-invoice-lookback-qa`
- Worker app: `ca-pvm-worker-qa`
- Queue: `acumatica-invoice-discovery`
- Alert: `alert-pvm-invoice-reconciliation-stale-qa`
- Action group: `ag-pvm-integrations-qa`

The incremental job runs every ten minutes with a 15-minute cursor overlap. The
daily job runs at 00:15 UTC and reloads the previous seven days. Both write a
persisted run and transactional outbox command before the worker calls
Acumatica.

## Deployment Checks

```powershell
az containerapp job show -g rg-pvm-integrations-qa -n job-pvm-invoice-reconcile-qa --output table
az containerapp job show -g rg-pvm-integrations-qa -n job-pvm-invoice-lookback-qa --output table
az containerapp job execution list -g rg-pvm-integrations-qa -n job-pvm-invoice-reconcile-qa --output table
az monitor scheduled-query show -g rg-pvm-integrations-qa -n alert-pvm-invoice-reconciliation-stale-qa
```

Confirm schedules `*/10 * * * *` and `15 0 * * *`, current worker image tags,
successful executions, and `Automation Disabled` in the Control Room.

## First Run

1. Select **Queue invoice reconciliation** in `/admin/messages`.
2. Open the accepted run from `/runs`.
3. Confirm `Query from` is no earlier than the configured invoice cutover and
   uses a seven-day bootstrap when no successful cursor exists.
4. Confirm the run reaches `Succeeded`, `Cursor after` equals `Query to`, and
   the outbox delivery is `Completed` with no dead letter.
5. Confirm the Control Room reports invoice sync `Healthy`.

## Incremental And Overlap Checks

1. Allow the next scheduled run to complete.
2. Confirm its `Cursor before` equals the previous successful `Cursor after`.
3. Confirm `Query from` is 15 minutes before that cursor and `Query to` is the
   current aligned ten-minute window.
4. Verify repeated records are counted as `Unchanged`, with no duplicate
   candidate or submission operation.
5. Finalize or update a QA Shoprite invoice whose modification timestamp falls
   inside the overlap. Confirm the next run creates or updates exactly one
   candidate and advances the cursor only after success.

## Daily Lookback Check

Start `job-pvm-invoice-lookback-qa` manually or wait for its schedule. Confirm
the run trigger is `daily-lookback`, its query covers seven days bounded by the
invoice cutover, and existing candidates remain single records.

## Failed Run Check

Use an approved reversible fault; do not alter shared credentials. Confirm a
failed Acumatica fetch:

- records a `Failed` run with a sanitized error;
- leaves `Cursor after` empty;
- does not replace the last successful high-water mark;
- preserves existing candidates and submission operations; and
- is retried only by normal message-delivery policy before dead-lettering.

Restore service and confirm the next successful run starts from the last
successful cursor minus overlap.

## Source-Version Check

For a QA candidate, change its Acumatica invoice after candidate preparation.
An automatic-mode command used in a controlled backend test must fetch the
current invoice and return `ManualReviewRequired` before any Shoprite POST.
Reconcile again and confirm the candidate source timestamp and canonical data
are updated. Do not enable the automatic policy in QA for this test.

## Freshness Alert

The 30-minute stateful alert uses successful reconciliation completion logs.
For a controlled fault test, pause only the incremental job, retain the daily
job, and record the previous schedule before changing it. Confirm the Control
Room changes to `Stale`, the alert action arrives, and no Shoprite submission is
made. Restore the schedule, run a reconciliation, confirm `Healthy`, and retain
the resolved-alert action evidence.

## Evidence To Retain

- deployment run, image tags, and job definitions;
- bootstrap, incremental, overlap, daily-lookback, and failed run IDs;
- cursor/query windows and candidate counts;
- queue active/dead-letter counts;
- source-version blocking evidence;
- alert fired/resolved action history; and
- Control Room and run-detail screenshots.
