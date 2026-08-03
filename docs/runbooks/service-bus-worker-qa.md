# Service Bus Worker QA Runbook

## Purpose

This runbook verifies the durable command path introduced for Shoprite production
automation without enabling scheduling or production invoice submission.

The worker consumes three commands:

| Queue | Contract | Current effect |
|---|---|---|
| `shoprite-po-refresh` | `shoprite.po-refresh.v1` | Fetches and idempotently upserts the current Shoprite PO batch. |
| `acumatica-invoice-discovery` | `acumatica.invoice-discovery.v1` | Runs the existing finalized-invoice refresh and matching path. |
| `shoprite-invoice-submit` | `shoprite.invoice-submit.v1` | Calls the same concurrency-safe submission handler used by manual submission. No automatic producer is enabled. |

## Safety State

- No timer, webhook, or automatic submission policy is enabled.
- The Admin console exposes manual PO refresh and invoice discovery commands only.
- The Admin console does not expose a queue-submission action.
- Shoprite submission mode remains environment-controlled.
- Message bodies contain identifiers and command context, not source invoices,
  generated XML, passwords, access tokens, or connection strings.

## Deployment Checks

After the QA deployment workflow completes:

```powershell
az servicebus queue list `
  --resource-group rg-pvm-integrations-qa `
  --namespace-name sb-pvm-integrations-qa `
  --query "[].{name:name,active:countDetails.activeMessageCount,dead:countDetails.deadLetterMessageCount}" `
  --output table

az containerapp show `
  --resource-group rg-pvm-integrations-qa `
  --name ca-pvm-worker-qa `
  --query "{state:properties.provisioningState,revision:properties.latestRevisionName}" `
  --output table
```

Expected queues:

- `shoprite-po-refresh`
- `acumatica-invoice-discovery`
- `shoprite-invoice-submit`

The worker has no ingress. One replica remains active because it must poll and
drain the PostgreSQL outbox even when all Service Bus queues are empty.

## Functional QA

1. Sign in to the QA Workbench as an Admin.
2. Open `/admin/messages`.
3. Select **Queue PO refresh**.
4. Confirm the outbox row progresses from `Pending` or `Publishing` to
   `Published`.
5. Confirm a delivery row reaches `Completed`.
6. Confirm the PO Inbox reflects the current Shoprite QA batch.
7. Select **Queue invoice discovery**.
8. Confirm the command and delivery reach `Published` and `Completed`.
9. Confirm finalized Acumatica QA invoices are refreshed through the existing
   candidate matching path.

Do not inject a `shoprite.invoice-submit.v1` message during this smoke test.
Submission automation is a later release gate.

## Failure Interpretation

| State | Meaning | Operator action |
|---|---|---|
| `Pending` | Waiting for the outbox dispatcher or a retry delay. | Check worker health if it does not advance. |
| `Publishing` | Leased by a worker. | Wait for the lease; a restarted worker reclaims stale leases. |
| `Retrying` | Handler failed before the delivery limit. | Inspect the safe error code and summary. |
| `DeadLettered` | Poison, invalid, or repeatedly failing message. | Correct the upstream/configuration issue before any replay tooling is introduced. |
| `Completed` | Handler reached a safe terminal result. | No action. Redelivery is harmless. |

The Admin view intentionally shows no raw broker payload and provides no replay
or destructive controls in this slice.

## Log Check

```powershell
az containerapp logs show `
  --resource-group rg-pvm-integrations-qa `
  --name ca-pvm-worker-qa `
  --type console `
  --tail 100
```

Logs may contain message IDs, queue names, and safe classifications. They must
not contain Shoprite query credentials, Acumatica passwords, authorization
headers, connection strings, invoice XML, or full source payloads.
