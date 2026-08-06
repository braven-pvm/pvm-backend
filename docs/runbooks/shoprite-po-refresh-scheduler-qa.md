# Shoprite PO Refresh Scheduler QA

Use this runbook after the Slice 4 deployment. Automatic invoice submission
must remain disabled throughout verification.

## Expected Resources

- Container Apps job: `job-pvm-po-refresh-qa`
- Worker app: `ca-pvm-worker-qa`
- Service Bus queue: `shoprite-po-refresh`
- Alert: `alert-pvm-po-refresh-stale-qa`
- Action group: `ag-pvm-integrations-qa`

The job runs every five minutes. Each execution creates one persisted
`shoprite-po-refresh` run and one outbox command. The always-on worker publishes
and consumes that command.

## Deployment Checks

```powershell
az containerapp job show -g rg-pvm-integrations-qa -n job-pvm-po-refresh-qa --output table
az containerapp job execution list -g rg-pvm-integrations-qa -n job-pvm-po-refresh-qa --output table
az monitor scheduled-query list -g rg-pvm-integrations-qa --output table
```

Confirm the schedule is `*/5 * * * *`, the worker image tag matches the current
release, and at least one job execution has succeeded.

## Workbench Checks

1. Open `/` and confirm the environment is `QA` and automation is `Disabled`.
2. Confirm PO data becomes `Healthy` after a successful refresh.
3. Open `/runs` and confirm a scheduled `shoprite-po-refresh` run reaches
   `Succeeded` with a message ID, timing, and result counts.
4. Select **Refresh POs** in `/purchase-orders`. Confirm it redirects to a new
   manual run and the same worker path completes it.
5. Open `/admin/messages` and confirm both commands are published and completed,
   with no retry or dead-letter record.

## Idempotency Check

Allow two scheduled refreshes against an unchanged Shoprite response. The later
run should report records under `Unchanged`, with no duplicate PO header or line
rows. Existing invoice candidates must remain linked.

## Failure and Freshness Check

Do not invalidate shared QA credentials merely to test failure. Use an approved
temporary configuration change or non-production fault injection window.

Confirm that:

- a failed fetch records a `Failed` run with a safe error summary;
- existing PO headers and lines remain available;
- PO freshness changes to `Stale` after 15 minutes without a successful run;
- automatic processing remains disallowed while stale; and
- the Azure Monitor alert reaches the configured operations email.

The alert evaluates every five minutes. It searches the previous day for the
most recent successful PO refresh and emits a numeric stale signal on every
evaluation: `0` while healthy and `1` when the timestamp is more than 15 minutes
old or no success exists in the lookback. The explicit healthy signal is needed
for reliable stateful alert resolution. Allow up to one evaluation interval
plus normal Azure Monitor log ingestion latency after crossing the boundary.

Restore the valid configuration, run a manual refresh, and confirm freshness
returns to `Healthy`. The stateful alert resolves after three healthy five-minute
evaluations; this is Azure Monitor's resolution period for a five-minute log
alert.

## Evidence to Retain

- deployed image tags and revision/job execution IDs;
- one scheduled and one manual run ID;
- unchanged-run counts;
- queue and dead-letter counts;
- alert fired/resolved evidence; and
- screenshots of Control Room, run detail, and PO freshness.
