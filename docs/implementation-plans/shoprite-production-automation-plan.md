# Shoprite Production Invoice Automation Plan

Last updated: 2026-08-07

Status: Approved for implementation on 2026-07-28

## Purpose

Move the verified Shoprite invoice integration from manually triggered QA
submission to a reliable, observable, and controllable automated production
service.

The production service will:

- discover finalized Shoprite invoices in Acumatica;
- keep a durable local Shoprite PO inbox current;
- match and validate each invoice against exactly one Shoprite PO;
- automatically submit only candidates that satisfy the production policy;
- route every exception or uncertain outcome to the workbench;
- prevent duplicate concurrent submissions;
- retain immutable request, response, audit, and state-transition evidence; and
- reconcile missed events so webhook delivery is never the only source of truth.

The production admin control plane defined in
`docs/implementation-plans/integration-admin-console-plan.md` is part of this
approved scope. Production automation cannot move beyond `Shadow` until the
required control, exception, audit, and emergency-stop workflows are
operational.

## Evidence and Starting Point

The QA vertical slice has crossed its primary contract-validation gate:

- real Shoprite QA `VendorOrder` retrieval works;
- real Acumatica QA finalized-invoice retrieval works;
- the real Acumatica invoice was matched to its Shoprite PO;
- item/GTIN and UOM mapping were completed through the workbench;
- the generated GS1 invoice XML was submitted to Shoprite QA; and
- Shoprite confirmed that the submitted invoice was structurally sound,
  correct, and verified.

This proves the source-to-payload mapping and Shoprite request contract for the
tested scenario. At the time of that initial evidence it did not prove
production concurrency safety, production credentials and connectivity,
automatic retries, event delivery, backup recovery, or operational response.

## Implementation Progress

As of 2026-08-07:

- Slice 1 is complete and deployed: explicit EF migrations, persisted submission
  operations, concurrency constraints, frozen source/payload versions, stale-send
  ambiguity, and shared manual submission are operational.
- Slice 2 is complete and deployed: Azure Blob payload archive, SHA-256
  verification, raw-body removal from operational PostgreSQL rows, immutable
  transition audit, and credential-content guards are operational.
- Deployed QA candidate `QA-INV-1212503708` produced one submitted operation,
  four independently hash-verified blobs, and the immutable transition sequence
  `Pending -> Sending -> Submitted`.
- The runtime verification used a deterministic PO-seeded candidate. The real
  Acumatica-source path was previously verified with `INV158888`; a combined
  post-archive real-Acumatica regression remains a pre-shadow gate.
- Slice 3 is complete, deployed, and runtime-verified in QA: Service Bus queues,
  transactional outbox dispatch, worker consumers, redelivery handling, and
  dead-letter metadata/read views are operational.
- Slice 4 is merged, deployed, and runtime-verified in QA. Its controlled fault
  test proved stale detection, policy blocking, action-group delivery, recovery,
  and alert resolution.
- Slice 5 is merged, deployed, and runtime-verified in QA. Bootstrap, daily
  lookback, and consecutive incremental windows advanced the persisted cursor
  without duplicate candidates or submission operations; the stale alert fired
  before recovery and resolved after healthy reconciliation.
- Slice 6 is implemented on `feature/acumatica-push-notification-ingestion` and
  awaits review and QA deployment. It adds authenticated bounded webhook receipt,
  a transactional event inbox/outbox, transaction-ID deduplication,
  authoritative per-invoice retrieval, Admin event visibility, and a QA setup
  and recovery runbook.
- Automatic invoice submission remains disabled.

## Decisions

### 1. Use a hybrid Acumatica trigger

Use an Acumatica push-notification webhook as the low-latency trigger and a
scheduled incremental reconciliation job as the completeness mechanism.

```text
Acumatica finalized-invoice change
  -> authenticated webhook
  -> durable event inbox/outbox
  -> Service Bus discovery command
  -> fetch authoritative invoice through Contract REST
  -> candidate match and validation
  -> auto-submit policy
```

In parallel:

```text
scheduled reconciliation
  -> query invoices changed since overlapping high-water mark
  -> pass each result through the same discovery command
```

The webhook is a hint that something changed. Its financial values are not used
as invoice truth. The worker fetches the current invoice through the Acumatica
Contract REST API before validation.

Reasons:

- Acumatica push notifications support JSON notifications to an HTTP webhook.
- The notification transaction `Id` can be used for duplicate detection.
- Acumatica documents failed webhook notification handling, but its resend
  retention is limited. A reconciliation poll is therefore still required.
- Reconciliation also detects configuration errors, disabled notifications,
  deployment outages, and records missed before the webhook was activated.

### 2. Keep Shoprite PO refresh scheduled

The documented Shoprite integration does not provide an event feed for
`VendorOrder`. Refresh it on a schedule and retain POs locally.

Initial production schedule:

- normal PO refresh: every 5 minutes;
- immediate recovery refresh: once when a finalized invoice has no matching
  local PO, followed by one revalidation;
- daily reconciliation: reload the available PO set and compare payload hashes.

Do not call `VendorOrder` for every invoice submission. Submission reads the
durable local PO record that was matched to the candidate.

### 3. Reuse one submission command

Manual and automatic submission must use the same application command and
state machine:

```text
SubmitShopriteInvoice(
  invoiceCandidateId,
  initiatedBy,
  initiationMode,
  correlationId
)
```

Supported initiation modes:

- `manual`
- `scheduled`
- `acumatica-push-notification`
- `reconciliation`
- `admin-retry`

The HTTP endpoint and queue consumer are adapters around this command. Business
validation, idempotency, payload generation, archiving, partner submission, and
attempt persistence remain inside the shared application path.

### 4. Prefer at-most-once submission when uncertain

Shoprite has not provided a server-side idempotency token contract. Exactly-once
delivery across PostgreSQL, Service Bus, and Shoprite is therefore not
technically claimable.

The system will target:

- effectively once when Shoprite returns a confirmed success;
- no concurrent duplicate sends for one business idempotency key; and
- at most once when the result of an attempted POST is uncertain.

A timeout, connection loss after sending may have started, worker termination
during a send, or unknown Shoprite response becomes `Ambiguous`. It is not
automatically retried.

### 5. Roll out automation through explicit modes

Production automation modes:

| Mode | Discovery | Validation | Automatic Shoprite POST |
| --- | --- | --- | --- |
| `Disabled` | Optional | Optional | Never |
| `Shadow` | Yes | Yes | Never |
| `Allowlisted` | Yes | Yes | Only configured accounts/locations |
| `Enabled` | Yes | Yes | All eligible configured Shoprite invoices |

Every new environment and deployment defaults to `Disabled`. Changing the mode
is an audited Admin action and an infrastructure configuration change.
`Disabled` and `Shadow` block automatic submission only. Authorized manual
submission remains available unless the emergency stop is active.

## Target Architecture

```text
                    +--------------------------+
                    | Acumatica Production     |
                    | finalized invoice + REST |
                    +------------+-------------+
                                 |
                    push notification / poll
                                 |
                    +------------v-------------+
                    | PVM API                   |
                    | webhook receiver          |
                    | admin/workbench API       |
                    +------------+-------------+
                                 |
                       DB inbox + outbox
                                 |
                    +------------v-------------+
                    | Azure Service Bus         |
                    | discovery / submit queues |
                    | dead-letter queues        |
                    +------------+-------------+
                                 |
                    +------------v-------------+
                    | PVM Worker                |
                    | PO refresh                |
                    | invoice refresh/match     |
                    | policy + submission       |
                    | reconciliation            |
                    +---+-----------+------------+
                        |           |
              +---------v--+   +----v----------------+
              | PostgreSQL |   | Blob payload archive |
              | state/audit|   | source/XML/responses |
              +------------+   +-----------------------+
                        |
                    +---v-------------------+
                    | Shoprite Production   |
                    | VendorOrder/Invoice   |
                    +-----------------------+
```

### Runtime components

| Component | Responsibility | Ingress |
| --- | --- | --- |
| API Container App | Workbench API and authenticated Acumatica webhook | Restricted external |
| Workbench Container App | Operators, mappings, exceptions, audit and controls | Entra-authenticated |
| Worker Container App | Service Bus consumers and outbox dispatch | None |
| Scheduled Container App Jobs | PO refresh and reconciliation command scheduling | None |
| PostgreSQL | Operational state, inbox/outbox, mappings, audit metadata | Private/restricted |
| Service Bus Standard | Durable command delivery and dead letters | Managed identity |
| Blob Storage | Immutable payload/source evidence | Managed identity |
| Key Vault | Partner, OAuth, webhook, and database secrets | Managed identity |

## End-to-End Production Flow

### A. Shoprite PO ingestion

1. The scheduler emits `RefreshShopritePurchaseOrders`.
2. The worker retrieves `VendorOrder`.
3. It upserts PO headers and lines by Shoprite PO number and line number.
4. It retains the raw response/payload hash and updates `LastSeenAt`.
5. It never deletes a locally linked PO merely because the next response omits
   it.
6. Changed/new POs cause affected `NeedsReview` candidates to be revalidated.

### B. Acumatica event ingestion

1. Acumatica sends a push notification to the production webhook endpoint.
2. The API validates TLS, the configured secret header, body size, company, and
   query name.
3. The notification transaction ID is deduplicated in `integration_event_inbox`.
4. The API stores the event and an outbox message in one PostgreSQL transaction.
5. It returns `202 Accepted` only after durable persistence.
6. The outbox dispatcher publishes an invoice-discovery command to Service Bus.

The Acumatica Generic Inquiry used for push notifications must be header-only
and simple:

- invoice internal ID or note ID;
- invoice type and reference number;
- status/released state;
- customer account;
- customer order/PO reference;
- last-modified timestamp.

Do not put invoice details, tax joins, aggregation, or formulas in the push
query. Fetch full invoice details through Contract REST.

### C. Scheduled invoice reconciliation

1. The scheduler emits `ReconcileAcumaticaInvoices`.
2. The worker reads the last successful high-water mark.
3. It queries records changed since `highWaterMark - overlap`.
4. Each result is processed through the same discovery handler used by webhook
   events.
5. The high-water mark advances only after the full page set is persisted.
6. A daily wider lookback detects delayed or corrected records.

Initial settings:

- incremental reconciliation: every 10 minutes;
- cursor overlap: 15 minutes;
- daily lookback: 7 days;
- page size: 100, tuned after production measurements.

Invoice date alone is not a valid production cursor because an older invoice can
be finalized or corrected later. The production endpoint or Generic Inquiry
must expose a reliable last-modified value.

### D. Candidate preparation

For every discovered invoice:

1. Fetch the invoice header, lines, and tax details from Acumatica.
2. Confirm it is an eligible finalized invoice for a configured Shoprite
   account.
3. Upsert the candidate by Acumatica internal invoice ID.
4. Match the captured PO number to exactly one local Shoprite PO.
5. Resolve delivery GLN and PO line context.
6. Apply verified item/GTIN, UOM, pack, and tax mappings.
7. Reconcile all line, tax, and grand totals.
8. Persist source, canonical, validation, and payload hashes.
9. Set `Ready` or `NeedsReview`.
10. Evaluate the automatic-submission policy.

Before an automatic send, fetch or verify the current Acumatica source version
again. If its version/hash changed after preparation, regenerate and revalidate
instead of sending the stale payload.

### E. Automatic-submission policy

A candidate can be automatically queued only when all conditions are true:

- automation mode permits its account and delivery location;
- Acumatica invoice is finalized and in the supported type/status;
- customer account belongs to the configured Shoprite scope;
- exactly one local Shoprite PO is matched;
- supplier and delivery GLNs are resolved;
- every item/GTIN mapping is verified;
- every UOM/pack mapping is verified;
- currency is `ZAR` and country is `ZA`;
- tax categories, percentages, line totals, VAT, and grand total reconcile;
- order type is explicitly supported;
- no catch-weight or other excluded scenario is detected;
- no prior successful or ambiguous submission exists;
- candidate source version is current; and
- optional `NotBefore` stabilization time has elapsed.

Initial allowlist policy:

- normal orders (`220`) only;
- configured Shoprite account and delivery-location allowlist;
- no allocation orders (`258`) until separately accepted in QA;
- no automatic retry after any attempted Shoprite POST;
- business-hours canary while an Admin is available.

Anything else remains visible as `NeedsReview`; it is never silently discarded.

### F. Concurrency-safe submission

The current manual flow checks for a previous attempt, sends, and then records
the attempt. Two concurrent workers could both pass the checks. Production must
replace this with a persisted submission operation.

Required design:

1. Create an immutable `submission_operation` generation for the business
   idempotency key.
2. Freeze the candidate source version, canonical version, generated XML, and
   payload hash for that operation.
3. Archive the request payload before external submission.
4. Atomically claim `Pending -> Sending` with a lease/row-version check.
5. Send only if this worker owns the claim.
6. Persist the response and terminal state.
7. Treat a stale `Sending` operation as `Ambiguous`, never as a safe retry.
8. Use a partial unique constraint so only one `Pending`, `Sending`,
   `Submitted`, or `Ambiguous` operation can exist for a business idempotency
   key.
9. A clearly `Rejected` operation is terminal. After the business data is fixed
   and revalidated, an Admin may create a new immutable operation generation
   under the same business key.
10. Service Bus redelivery loads the existing operation and applies its state;
    it does not create another external send.

The business idempotency key remains:

```text
supplier GLN
  + delivery location GLN
  + Shoprite PO number
  + Acumatica invoice number
```

Add the key and payload hash to every operation and attempt record.

## State Model

Candidate states:

```text
Discovered
  -> NeedsReview
  -> Ready
  -> Queued
  -> Submitting
  -> Submitted
  -> Rejected
  -> Ambiguous
  -> Suspended
```

Rules:

- `NeedsReview -> Ready` requires successful revalidation.
- `Ready -> Queued` requires the current automation policy or a manual action.
- `Queued -> Submitting` requires an atomic submission-operation claim.
- `Submitting -> Submitted` requires confirmed Shoprite acceptance.
- `Submitting -> Rejected` requires a clear, classified rejection response.
- `Submitting -> Ambiguous` covers any uncertain external outcome.
- `Submitted` and `Ambiguous` are never automatically requeued.
- `Rejected -> NeedsReview` requires a source/config correction or explicit
  Admin review; a new operation generation can be created only after successful
  revalidation.
- `Ambiguous -> Submitted` is allowed only when an Admin obtains evidence that
  Shoprite accepted the invoice.
- `Ambiguous -> NeedsReview` is allowed only when an Admin obtains evidence
  that Shoprite did not receive or accept it.
- `Suspended` is used by the kill switch or an Admin hold.

Every transition records actor, mode, correlation ID, previous/new state,
reason, timestamp, source version, and payload hash.

## Failure and Retry Policy

| Failure | Automatic action | Result |
| --- | --- | --- |
| Duplicate webhook/queue message | Deduplicate | Existing state retained |
| Acumatica unavailable before candidate creation | Exponential retry, then DLQ | No Shoprite side effect |
| Shoprite PO refresh unavailable | Retry refresh; candidate waits | `NeedsReview`/waiting |
| Mapping or financial validation failure | No retry until data/config changes | `NeedsReview` |
| Worker fails before claiming submission | Queue redelivery | Safe |
| Worker fails after claim but before POST starts | Lease-aware recovery | Safe only if no send began |
| Timeout/network loss after POST may have started | No automatic retry | `Ambiguous` |
| Worker dies during Shoprite POST | No automatic retry | Stale `Sending -> Ambiguous` |
| Clear Shoprite validation rejection | No automatic retry; fix, revalidate, and require Admin retry | `Rejected` |
| Confirmed Shoprite acceptance | Complete message | `Submitted` |
| Poison/unknown processing error | Bounded retry, then DLQ | Admin task and alert |

An automatic retry after a Shoprite POST may be added only after Shoprite
documents duplicate handling or supports a usable idempotency contract.

## Service Bus Design

Provision:

- `shoprite-po-refresh`
- `acumatica-invoice-discovery`
- `shoprite-invoice-submit`

Queue settings:

- duplicate detection enabled where supported;
- message IDs derived from event ID, invoice source ID, or submission operation
  ID;
- bounded delivery count;
- dead lettering on expiration and poison processing;
- lock renewal configured for known worker execution times;
- managed-identity RBAC, not connection strings;
- correlation and causation IDs on every message.

Service Bus duplicate detection is a transport safeguard. PostgreSQL operation
state remains the authority preventing a duplicate external POST.

## Persistence and Migrations

Before production:

- replace runtime `EnsureCreated` schema management with explicit EF migrations;
- add `integration_event_inbox`;
- add `integration_outbox`;
- add `integration_runs` and high-water marks;
- add `submission_operations`;
- extend attempts with operation ID, idempotency key, correlation ID, source
  version, and payload hash;
- add candidate source-version/concurrency fields;
- add audited automation policy/configuration;
- add indexes for queue/status/age/admin views;
- test forward migration and rollback/restore procedures.

Production deployment must run migrations as a controlled job, not concurrently
from every API/worker replica.

## Payload Archive

Move raw payload bodies out of PostgreSQL for production:

```text
payloads/acumatica/invoices/{yyyy}/{MM}/{candidateId}/{sourceVersion}.json
payloads/shoprite/invoices/{yyyy}/{MM}/{operationId}/request.xml
payloads/shoprite/invoices/{yyyy}/{MM}/{operationId}/response.txt
payloads/shoprite/purchase-orders/{yyyy}/{MM}/{refreshId}.json
```

PostgreSQL stores:

- blob location;
- SHA-256 hash;
- content type;
- byte count;
- created timestamp;
- operation/candidate linkage.

Enable blob versioning, soft delete, lifecycle rules, and retention agreed with
finance/audit. Never archive credentials, auth headers, or full request URLs.

## Security

### Acumatica

- Use a dedicated least-privilege production integration user.
- Register a Connected Application and validate the supported OAuth flow with
  the Acumatica provider; do not assume client-credentials support.
- Restrict the user to required customer/invoice read entities and tenant.
- Store tokens/secrets in Key Vault and rotate them.
- Pin and contract-test the production endpoint version before go-live.

### Acumatica webhook

- HTTPS only.
- Validate a dedicated secret header configured in Acumatica Push
  Notifications.
- Rotate the webhook secret independently.
- Validate company/query allowlists and request size.
- Rate-limit the endpoint and restrict source IPs when stable source ranges are
  available.
- Return no sensitive details.

### Shoprite

Shoprite requires credentials in query parameters. Before production:

- ensure application, reverse-proxy, Container Apps, App Insights, and exception
  logging redact `userName` and `password`;
- do not store the outbound URI;
- rotate QA credentials out of production;
- keep production credentials only in Key Vault;
- confirm production URL, supplier identity, support contacts, and access
  allowlisting with Shoprite.

### Azure

- separate production resource group and data stores from QA;
- separate managed identities for deployer, API, worker, and jobs where useful;
- private/restricted PostgreSQL, Blob, Service Bus, and Key Vault access;
- public ingress only for the Entra-authenticated workbench and authenticated
  Acumatica webhook/API route;
- managed identity for Service Bus, Blob, Key Vault, and ACR;
- production budget and cost alerts.

## Production Infrastructure

Create a separate production stack from the same Bicep modules:

```text
rg-pvm-integrations-prod
cae-pvm-integrations-prod
ca-pvm-api-prod
ca-pvm-workbench-prod
ca-pvm-worker-prod
job-pvm-po-refresh-prod
job-pvm-invoice-reconcile-prod
psql-pvm-integrations-prod
sb-pvm-integrations-prod
stpvmintegrationsprod
kv-pvm-intg-prod
appi-pvm-integrations-prod
log-pvm-integrations-prod
```

Production differences from QA:

- no fixture or QA connector modes;
- min replicas sized to remove event-processing cold starts;
- PostgreSQL point-in-time restore and production backup retention;
- high availability evaluated against recovery objectives;
- storage auto-grow enabled;
- public `0.0.0.0/0` PostgreSQL firewall rule prohibited;
- Service Bus queues and worker deployed;
- payload archive active;
- dependency health/readiness checks;
- alerts and action groups;
- deployment slots/revisions with smoke and rollback gates.

## Observability and Admin Operations

Detailed screen, role, workflow, API, accessibility, and implementation
requirements are defined in
`docs/implementation-plans/integration-admin-console-plan.md`.

Dashboard metrics:

- last successful Shoprite PO refresh;
- last Acumatica webhook and reconciliation times;
- webhook lag and duplicate count;
- candidate counts by state;
- ready-to-queued and queued-to-submitted latency;
- submissions by accepted/rejected/ambiguous status;
- oldest unresolved review, failure, ambiguous, and DLQ item;
- Service Bus active/dead-letter counts;
- Acumatica and Shoprite latency/error rates;
- reconciliation differences.

Required alerts:

- no successful PO refresh within 15 minutes;
- no successful invoice reconciliation within 30 minutes;
- any ambiguous submission;
- any dead-lettered submission;
- consecutive partner authentication failures;
- queue age above threshold;
- database/storage/worker health failure;
- payload archive failure;
- automated mode changed.

Workbench additions:

- automation mode and kill switch;
- integration run history;
- queue and dead-letter views;
- candidate state timeline;
- manual hold/release;
- explicit ambiguous-outcome resolution;
- safe-retry action only for classified eligible failures;
- payload hash/archive links;
- reconciliation status;
- audited policy and allowlist management.

The console is a functional command surface, not a telemetry-only dashboard.
All controls call the same backend command handlers used by automated workers.
Admin has full controlled access, while the API continues to enforce state,
validation, idempotency, reason, and audit requirements.

## Implementation Slices

### Slice 1: Production persistence and submission safety

Deliver:

- explicit EF migrations;
- submission-operation state machine;
- database concurrency/idempotency constraints;
- frozen payload/source version;
- concurrency and crash-boundary tests.

Acceptance:

- 20 concurrent commands for one candidate produce at most one Shoprite POST;
- queue redelivery does not resend a completed/ambiguous operation;
- stale `Sending` becomes `Ambiguous`;
- manual submission still uses the same command.

### Slice 2: Payload archive and audit

Deliver:

- Blob Storage archive service;
- request/response/source storage;
- hash verification;
- immutable transition audit;
- redaction tests.

Acceptance:

- every operation can be reconstructed from metadata and blobs;
- hashes verify;
- no credential or full credential-bearing URI appears in logs or archive.

### Slice 3: Service Bus and worker runtime

Deliver:

- queue resources and managed-identity roles in Bicep;
- worker project/container;
- outbox dispatcher;
- discovery and submission consumers;
- DLQ metadata and workbench read views.

Acceptance:

- duplicate/redelivered messages are harmless;
- worker restart resumes pending work;
- poison messages dead-letter with actionable reason.

### Slice 4: Scheduled Shoprite PO refresh

Deliver:

- scheduled job/command;
- refresh run records;
- changed-PO candidate revalidation;
- freshness metric and alert.

Acceptance:

- repeated identical refreshes do not duplicate POs/lines;
- unavailable Shoprite leaves existing PO context intact;
- stale PO data is visible and blocks policy when beyond configured age.

### Slice 5: Incremental Acumatica reconciliation

Deliver:

- per-invoice fetch;
- last-modified cursor and overlap;
- run/high-water persistence;
- daily wider lookback;
- source-version checks.

Acceptance:

- late-finalized and updated invoices are found;
- overlapping runs do not duplicate candidates or submissions;
- cursor advances only after a complete successful run.

### Slice 6: Acumatica push-notification ingestion

Deliver:

- header-only production/QA Generic Inquiry definition and setup runbook;
- authenticated webhook endpoint;
- inbox/outbox deduplication;
- event-to-discovery flow;
- failed-notification recovery test.

Acceptance:

- duplicate and out-of-order notifications are harmless;
- endpoint returns only after durable persistence;
- notification causes authoritative REST retrieval, not direct submission.

### Slice 7: Automation policy and shadow mode

Deliver:

- persisted/audited automation mode;
- account/location/order-type allowlists;
- stabilization delay;
- shadow decisions and comparison report;
- global kill switch.

Acceptance:

- automatic policy in `Disabled` and `Shadow` can never call Shoprite;
- authorized manual submission remains available unless emergency stop is
  active;
- only allowlisted candidates queue in canary mode;
- policy changes are audited and immediately enforceable.

### Slice 8: Exception operations

Deliver:

- ambiguous resolution;
- manual hold/release;
- rejected/error classification;
- safe retry command;
- DLQ replay with state checks.

Acceptance:

- no exception action bypasses validation or idempotency;
- every Admin action records reason and actor;
- ambiguous submissions cannot be retried without explicit resolution.

The user-facing exception center and task workflows for this slice are defined
in Console Slice C of
`docs/implementation-plans/integration-admin-console-plan.md`.

### Slice 9: Production infrastructure and release pipeline

Deliver:

- parameterized production Bicep;
- private/restricted network configuration;
- production database/backup/restore setup;
- worker/jobs/queues;
- environment-scoped GitHub deployment;
- smoke, migration, rollback, and approval gates.

Acceptance:

- QA and production credentials/data/resources are isolated;
- deployment starts in `Disabled`;
- backup restore is tested;
- previous application revision can be restored without replaying submissions.

### Slice 10: Shadow, canary, and go-live

Deliver:

- production read-only connectivity tests;
- shadow comparison;
- allowlisted canary;
- operational runbook and support roster;
- full enablement approval record.

Acceptance:

- all go-live gates below pass;
- canary invoices are independently confirmed in Acumatica, PVM, and Shoprite;
- alerts and kill switch are exercised before full enablement.
- production console go-live gates pass before automation moves beyond
  `Shadow`.

## Test Strategy

Required automated tests:

- candidate and submission state transition tests;
- PostgreSQL uniqueness/concurrency tests;
- simultaneous worker/command test proving one outbound call;
- outbox atomicity and replay tests;
- duplicate/out-of-order webhook tests;
- reconciliation cursor/overlap tests;
- stale-source-before-send test;
- exact XML golden/contract tests from accepted QA evidence;
- Shoprite response classification tests;
- timeout/crash ambiguity tests;
- payload hash/archive tests;
- logging redaction tests;
- authorization and Admin-action audit tests;
- Bicep validation and environment isolation checks.

Required environment tests:

- Acumatica production read-only contract smoke;
- Shoprite production authentication/connectivity test without submitting;
- QA end-to-end event-to-submission rehearsal;
- Service Bus retry/DLQ exercise;
- worker restart during each pre-send boundary;
- simulated timeout after send starts;
- PostgreSQL restore and blob retrieval;
- Container Apps revision rollback;
- kill-switch exercise.

## Rollout

### Gate 0: QA evidence complete

Status: Passed for the tested normal-order invoice scenario.

Evidence:

- real Acumatica-source invoice;
- real Shoprite QA PO;
- verified mappings;
- accepted invoice submission;
- Shoprite structural/correctness confirmation.

### Gate 1: Production connectivity, no sends

- production resources deployed;
- Acumatica read-only connection verified;
- Shoprite production credentials verified without invoice POST;
- production endpoint contracts captured;
- logging redaction verified;
- automation `Disabled`.

### Gate 2: Shadow mode

Run for at least five normal business days or a representative invoice volume:

- discover and prepare invoices automatically;
- perform no Shoprite POST;
- compare candidate set, mappings, totals, and expected timing with Acumatica;
- resolve all unexpected `NeedsReview` cases;
- prove reconciliation catches intentionally suppressed webhook events.

### Gate 3: Allowlisted canary

- one approved Shoprite account/location cohort;
- normal orders only;
- business hours with Admin and Shoprite support available;
- begin with one invoice, then a small daily cap;
- independently confirm every result in Shoprite;
- stop immediately on ambiguous, duplicate, reconciliation, or totals anomaly.

### Gate 4: Controlled expansion

- add account/location cohorts explicitly;
- review metrics and exception rate after each expansion;
- keep unsupported order/item scenarios manual.

### Gate 5: Enabled

Enable all configured eligible Shoprite invoices only after written operational
approval. The kill switch and manual fallback remain permanent.

## Go-Live Gates

All are required:

- Shoprite production endpoint, credentials, support path, and supplier identity
  confirmed;
- Acumatica production tenant, endpoint version, integration user/OAuth flow,
  account scope, and last-modified field confirmed;
- accepted QA XML retained as a regression fixture;
- production response parser recognizes confirmed acceptance and rejection;
- concurrency test proves one external POST;
- ambiguous resolution is operational;
- payload archive and audit are complete;
- explicit migrations and restore test pass;
- Service Bus queues, DLQs, worker, and outbox are operational;
- webhook and reconciliation both pass;
- `Disabled`, `Shadow`, allowlist, and kill-switch tests pass;
- production logs contain no credentials;
- monitoring and on-call notifications are active;
- the production control room, exception center, run history, automation
  controls, audit explorer, and emergency stop are operational;
- runbook, support contacts, and rollback steps are approved;
- shadow and canary gates pass.

## Rollback and Kill Switch

Rollback means stopping new automated sends, not reversing invoices already
accepted by Shoprite.

Immediate response:

1. Set automation mode to `Disabled`.
2. Stop/deactivate submission queue consumers.
3. Leave discovery and reconciliation running unless their data is suspect.
4. Preserve queue messages, operations, attempts, and payload evidence.
5. Classify in-flight `Sending` operations as `Ambiguous`.
6. Revert the application revision only after state is preserved.
7. Resume manual submission for validated candidates if approved.

Never purge queues or delete attempts as part of rollback.

## Inputs Required Before Production Connectivity

From Shoprite:

- production `VendorOrder` and `VendorInvoice` base URL;
- production username/password and activation date;
- confirmation of production supplier GLN/VAT identity;
- expected success/rejection response examples;
- rate limits and maintenance windows;
- duplicate/replay behavior;
- source/destination allowlisting requirements;
- production support/escalation contacts.

From Acumatica/IT provider:

- production instance URL, tenant/company, and branch;
- production endpoint name/version and exposed fields;
- dedicated least-privilege integration user;
- Connected Application and supported OAuth flow;
- permission to configure the Generic Inquiry and Push Notification;
- reliable last-modified field;
- outbound webhook network path and source IP details if available;
- upgrade/change-window ownership.

From PVM:

- production Shoprite account and delivery-location allowlist;
- supported order types for first release;
- preferred automation stabilization delay;
- business-hours canary window;
- operational Admins and alert recipients;
- retention and recovery objectives;
- final go-live approver.

## Recommended Immediate Next Work

Complete Slice 6 review and QA runtime verification using
`docs/runbooks/acumatica-push-notifications-qa.md`. First prove the deployed
endpoint with synthetic notifications, then activate the Acumatica QA
destination for a controlled real-origin event and recovery check. Do not enable
automatic invoice submission during this verification.

## References

Internal:

- `docs/spec-slices/shoprite-invoice-upload-mvp.md`
- `docs/spec-slices/shoprite-po-pivot-invoice-submission.md`
- `docs/implementation-plans/integration-admin-console-plan.md`
- `docs/runbooks/shoprite-qa-submission.md`
- `docs/architecture-stack-options.md`

Acumatica:

- Contract REST configuration:
  `https://help.acumatica.com/Wiki/ShowWiki.aspx?PageID=91dda8ed-5e92-48a5-a176-9a255506d0d6&wikiname=HelpRoot_Dev_Integration`
- OAuth/OIDC authorization:
  `https://help-2025r2.acumatica.com/Wiki/ShowWiki.aspx?PageID=a8f71c44-9f5c-4af8-9d47-bc815c8a58e7&wikiname=HelpRoot_Dev_Integration`
- Integration Development Guide, push notifications:
  `https://www.acumatica.com/media/2020/09/AcumaticaERP_IntegrationDevelopmentGuide.pdf`
