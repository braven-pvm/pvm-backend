# PVM Integration Admin Console Plan

Last updated: 2026-07-28

Status: Approved release requirement; detailed plan ready for implementation

## Purpose

Turn the existing PVM Invoice Workbench into the operational control plane for
the Acumatica and Shoprite integration.

The console must let authorized staff answer, without database or Azure access:

- Is automation running?
- Are Acumatica and Shoprite current and reachable?
- What work is pending, blocked, rejected, ambiguous, or dead-lettered?
- Why is a document blocked?
- What exactly was read, generated, and sent?
- Who or what changed the state?
- Is a retry safe?
- How can new submissions be stopped immediately?
- What needs to be fixed in Acumatica versus integration configuration?

The console is part of the reliability model. It is not a presentation-only
dashboard and it must not create a second business-logic path.

## Product Position

The existing Next.js workbench remains the frontend. The .NET API and worker
remain the system of record and execution boundary.

Rules:

- Every mutation is a server-side command.
- Manual and automatic actions use the same application handlers.
- The UI cannot bypass validation, idempotency, operation claims, outbox, or
  audit rules.
- The UI never edits Acumatica financial values or raw Shoprite XML.
- All control actions are permission-checked and audited in the API.
- Production state must remain understandable when Azure Portal and
  Application Insights are unavailable to an operator.
- Azure telemetry is linked for deep diagnosis, not used as the only
  operational record.

## Current Baseline

Implemented:

- Microsoft Entra sign-in.
- App-managed `Admin`, `Operator`, and `Viewer` roles.
- Invoice candidate list and detail.
- Validation issues and generated XML preview.
- Manual submission and attempt history.
- Shoprite PO inbox list and detail.
- Manual invoice and PO refresh.
- Reusable inventory-level item/GTIN and UOM mapping with Admin verification,
  impact counts, audit reasons, and affected-candidate revalidation.
- Admin user management.

Missing:

- Operational overview.
- Automation mode and emergency stop.
- Integration run and high-water-mark history.
- Queue depth, age, and dead-letter visibility.
- Unified exception/task workflow.
- Ambiguous resolution and classified safe retry.
- Remaining global location/GLN, pack, and tax mapping management.
- Connection health and configuration status.
- State-transition and configuration audit explorer.
- Correlation-driven payload/trace navigation.
- Search, filtering, pagination, and export at production volume.

## Users and Roles

### Viewer

Read-only operational access:

- control room;
- invoice and PO records;
- validation and state timelines;
- redacted payloads;
- attempts, runs, exceptions, connection health, and audit;
- mappings and automation policy.

Viewer cannot execute commands or change configuration.

### Operator

Viewer access plus routine operations:

- manually refresh Shoprite POs;
- manually reconcile Acumatica invoices;
- revalidate candidates;
- submit a valid invoice manually;
- place and remove a document hold;
- claim and comment on exception tasks;
- execute a retry only when the backend classifies it as safe;
- identify mapping problems and route them to an Admin.

Operator cannot:

- enable production automation;
- change global partner/account policy;
- create, edit, or verify production mappings;
- resolve an ambiguous submission;
- replay dead letters that may have reached Shoprite;
- manage users or connection credentials.

### Admin

Full console access:

- every Viewer and Operator capability;
- automation mode and emergency stop;
- account, location, order-type, and schedule policy;
- all mappings and verification state;
- ambiguous-outcome resolution;
- dead-letter disposition and controlled replay;
- connection configuration metadata and connection tests;
- user and role administration;
- data exports and payload access;
- operational thresholds and alert recipients.

Full access does not mean bypass access. Admin commands still enforce validation,
idempotency, state transitions, reason capture, and audit.

## Information Architecture

Use a persistent left navigation on desktop and a drawer on narrow screens.
The current horizontal navigation will not scale to the required operational
surface.

```text
Control room

Operations
  Invoices
  PO inbox
  Exceptions
  Runs

Configuration
  Mappings
  Automation
  Connections

Administration
  Audit
  Users
```

Global shell:

- PVM Workbench identity.
- Environment indicator: `QA` or `PRODUCTION`.
- Current automation mode.
- Global search.
- Active incident/paused banner.
- Signed-in user, role, and sign-out.

Production must be visually unmistakable through explicit text and iconography,
not colour alone.

## Route Plan

| Route | Purpose |
| --- | --- |
| `/` | Control room |
| `/invoices` | Searchable candidate and submission queue |
| `/invoices/{id}` | Invoice truth, PO context, validation, payload, attempts, timeline and actions |
| `/purchase-orders` | Durable Shoprite PO inbox and freshness |
| `/purchase-orders/{id}` | PO header/lines, payload, linked invoices and history |
| `/exceptions` | Unified operational tasks and exception queues |
| `/exceptions/{id}` | Diagnosis, ownership, evidence, resolution and commands |
| `/runs` | Refresh, reconciliation, webhook, submission and maintenance runs |
| `/runs/{id}` | Run inputs, counts, cursor, messages, errors and correlation links |
| `/mappings/items` | Item/GTIN mappings |
| `/mappings/uom` | UOM/pack mappings |
| `/mappings/locations` | Account/location/GLN mappings |
| `/mappings/tax` | Tax mappings |
| `/automation` | Mode, allowlists, schedules, thresholds and emergency controls |
| `/connections` | Connector configuration status and health checks |
| `/audit` | Immutable action and state-transition history |
| `/admin/users` | Pre-authorized users and app roles |

Payloads and traces are contextual views from an invoice, PO, run, operation,
attempt, or exception. They are not a separate top-level destination.

## Screen Specifications

### 1. Control Room

Primary job: understand current operating state and move to the most urgent
actionable work.

Top status band:

- environment;
- automation mode;
- overall health;
- last successful PO refresh;
- last successful invoice reconciliation;
- oldest queued submission;
- open ambiguous count;
- dead-letter count.

Main layout:

1. Active incident or paused-state banner.
2. Compact operational status band.
3. Urgent work table ordered by severity and age.
4. Connector and pipeline health matrix.
5. Recent integration runs.
6. Submission outcomes for the selected period.
7. Emergency stop and links to detailed Admin controls, visible only to Admin.

Keep policy/configuration forms on the Automation page. The control room should
surface state and urgent actions without becoming a dense settings page.

Health matrix rows:

- Acumatica authentication/API;
- Acumatica webhook;
- invoice reconciliation;
- Shoprite `VendorOrder`;
- Shoprite `VendorInvoice`;
- PostgreSQL;
- Service Bus discovery queue;
- Service Bus submission queue;
- Blob payload archive;
- worker heartbeat.

Each row shows:

- `Healthy`, `Degraded`, `Failed`, `Paused`, or `Unknown`;
- last success;
- current age/lag;
- latest failure summary;
- link to the relevant run or exception.

Do not build decorative charts before there is enough history to support a real
trend. Initial submission outcomes can be a compact table or restrained bar
summary.

States:

- healthy with no urgent work;
- degraded dependency;
- automation paused;
- shadow mode;
- urgent exceptions;
- stale data;
- partial telemetry;
- no permission;
- first-run/empty state.

### 2. Invoice Queue

Primary job: find and inspect an invoice that needs attention.

Filters:

- invoice number;
- Shoprite PO;
- customer account;
- delivery location/GLN;
- status;
- validation issue;
- order type;
- initiation mode;
- updated/submitted date;
- held/not held.

Default columns:

- invoice;
- Acumatica customer;
- Shoprite PO;
- delivery location;
- current state;
- blocking issue count;
- source age;
- latest attempt;
- updated time;
- action.

Production behavior:

- server-side filtering, sorting, and cursor pagination;
- filters encoded in the URL;
- total and filtered counts;
- stale-source indicator;
- status icon and text, never colour alone;
- no bulk invoice submission;
- optional bulk revalidation or hold only after backend support and audit exist.

Empty states distinguish:

- no candidates exist;
- filters returned no candidates;
- source is stale;
- source refresh failed.

### 3. Invoice Detail

Primary job: decide the next safe action for one invoice.

Header:

- invoice number and state;
- Acumatica customer;
- Shoprite PO/location;
- source version and freshness;
- held/automation eligibility;
- one context-appropriate primary action.

Sections:

- Status timeline.
- Validation and fix guidance.
- Acumatica invoice truth.
- Matched Shoprite PO and delivery context.
- Line reconciliation and mappings.
- Financial reconciliation.
- Generated Shoprite payload.
- Submission operation and attempts.
- Source, request, and response payload evidence.
- Audit events and correlation links.

Validation rows show:

- severity;
- stable code;
- plain-language problem;
- fix location: Acumatica, mapping/configuration, or external support;
- affected line/field;
- action where the console can resolve it.

Action rules:

- `Revalidate invoice` for changed source/config.
- `Submit invoice` only when backend says `canSubmit`.
- `Place hold` is reversible and requires a reason.
- `Retry submission` appears only for backend-classified safe failures.
- `Resolve ambiguous outcome` is Admin-only and requires evidence.
- `View in Acumatica` may deep-link when a stable production URL is available.

Payload viewer:

- generated XML is read-only;
- redacted by default;
- copy/download permission is explicit;
- request and response hashes are visible;
- archive and trace links are visible;
- large payloads use a bounded, searchable viewer;
- no credentials or credential-bearing URI.

### 4. Shoprite PO Inbox

Primary job: confirm that the PO context required by invoices is present and
fresh.

List filters:

- PO number;
- order type;
- location name/code/GLN;
- supplier GLN;
- GTIN/item;
- first/last seen;
- linked/unlinked;
- stale/current.

The list shows PO freshness and linked invoice state. The detail view shows:

- PO header and delivery-source resolution;
- PO lines;
- linked invoices;
- first/last seen history;
- payload hash and redacted raw JSON;
- refresh/run correlation;
- unsupported-order warning;
- acknowledgement state when that later scope is implemented.

`Refresh POs` creates an integration run and redirects to its result or shows
the accepted run ID. It must not make the browser wait on an unbounded partner
request.

### 5. Exceptions

Primary job: work unresolved operational problems to a safe disposition.

Tabs:

- Needs review.
- Rejected.
- Ambiguous.
- Dead letters.
- Stuck/overdue.
- Resolved.

Default ordering:

1. Ambiguous submissions.
2. Potential duplicate or financial mismatch.
3. Dead-lettered submissions.
4. Authentication/connector failures.
5. Stale or stuck work.
6. Mapping and source-data problems.

Exception/task fields:

- severity and category;
- linked document/run/message;
- stable error code;
- plain-language summary;
- fix location;
- retry classification;
- owner;
- status;
- age and due threshold;
- latest evidence;
- comments and resolution reason.

Task states:

```text
Open -> In progress -> Resolved
  \-> Waiting for Acumatica
  \-> Waiting for Shoprite
  \-> Suppressed by Admin
```

Repeated refreshes must update the same open task by deduplication key rather
than create duplicates.

Ambiguous resolution flow:

1. Show invoice, PO, operation, request hash, response evidence, and timeline.
2. Require Admin to record how Shoprite was checked.
3. Choose `Confirmed accepted`, `Confirmed not accepted`, or `Still unknown`.
4. Show the exact state consequence before confirmation.
5. Record evidence and reason.
6. Never expose a generic retry action while outcome remains unknown.

Dead-letter flow:

1. Show delivery count, reason, source message, operation state, and side-effect
   boundary.
2. Backend evaluates whether replay is safe.
3. `Replay message` is available only when safe.
4. Otherwise resolve/escalate without replay.

### 6. Integration Runs

Primary job: understand what an automated or manual process did.

Run types:

- Shoprite PO refresh;
- Acumatica webhook ingestion;
- Acumatica incremental reconciliation;
- daily lookback reconciliation;
- invoice discovery/preparation;
- submission dispatch;
- outbox dispatch;
- payload archive;
- maintenance/migration.

List columns:

- run ID/type;
- trigger and actor;
- status;
- started/completed;
- duration;
- received/created/updated/skipped/failed counts;
- high-water mark;
- correlation ID.

Run detail:

- input scope and configuration version;
- start/end cursors;
- page/message counts;
- affected records;
- warnings/errors;
- created exceptions;
- queue and trace links;
- safe rerun command when supported.

Run status is persisted operational data. It is not inferred only from logs.

### 7. Mappings

Primary job: manage reusable integration translations and understand their
impact.

Mapping views:

- item/GTIN;
- UOM/pack;
- account/location/GLN;
- tax.

Common columns:

- source key;
- Shoprite target;
- verification state;
- environment/scope;
- usage count;
- affected open candidates;
- last changed by/at.

Edit flow:

1. Show current mapping and provenance.
2. Show affected candidates and projected revalidation impact.
3. Capture new value and reason.
4. Confirm the mapping.
5. Persist a versioned audit event.
6. Revalidate affected candidates asynchronously.
7. Show the resulting run and exceptions.

Production requires verified mappings. No mapping edit changes Acumatica
invoice quantities, prices, tax, totals, or invoice number.

### 8. Automation

Primary job: control what automation is permitted to do.

Sections:

- current mode;
- emergency stop;
- account/location allowlist;
- supported order types;
- schedules;
- stabilization delay;
- freshness thresholds;
- daily caps/canary limits;
- active configuration version;
- recent policy changes.

Mode control:

- segmented control for `Disabled`, `Shadow`, `Allowlisted`, and `Enabled`;
- production environment and current mode repeated in the confirmation;
- impact preview: eligible candidates, queued work, excluded cohorts;
- reason required;
- mode change audited;
- API applies the change atomically;
- new automatic submissions stop immediately when set to `Disabled`;
- manual submission remains available to authorized users unless the emergency
  stop is active;
- discovery/reconciliation behavior is stated separately.

Emergency stop:

- always visible to Admin on production pages;
- stops automatic and manual submission claims immediately;
- does not purge queues or mutate submitted invoices;
- requires a confirmation dialog and reason;
- emits a high-severity audit event and alert;
- remains active across deployments.

Enabling `Enabled` in production requires stronger friction:

- explicit environment confirmation;
- review of the affected cohorts;
- checkbox acknowledging that invoices will be sent automatically;
- typed confirmation or equivalent high-friction control;
- reason and change reference.

### 9. Connections

Primary job: verify connector readiness without exposing secrets.

Connections:

- Acumatica;
- Shoprite;
- PostgreSQL;
- Service Bus;
- Blob Storage;
- Application Insights/Log Analytics.

Show:

- configured/missing status;
- environment and endpoint host;
- endpoint/contract version;
- authentication method;
- secret reference name, never value;
- last successful call;
- last failed call and classification;
- latency;
- last connection test;
- linked recent runs/exceptions.

Admin actions:

- `Test Acumatica connection`;
- `Test Shoprite authentication` using a non-submitting operation;
- `Test payload archive`;
- `Test Service Bus`;
- rotate/update a secret reference through the approved deployment process.

Connection tests create audited integration runs. They do not run in the page
request.

### 10. Audit

Primary job: prove what changed, who caused it, and the before/after state.

Filters:

- actor;
- automated/manual;
- action;
- entity type/reference;
- environment;
- date;
- correlation ID;
- configuration version.

Audit detail:

- actor and role;
- command and initiation mode;
- reason;
- timestamp;
- entity and state transition;
- redacted before/after;
- source/payload hashes;
- operation/run/message correlation;
- related exception.

Audit events are immutable. Corrections append events; they do not edit history.

### 11. Users

Retain pre-authorized Microsoft accounts and app-managed roles.

Improvements:

- filter and search;
- explicit last login and status;
- role descriptions;
- reason capture for disable/role changes;
- prevent removal of the final active Admin;
- show user audit timeline;
- separate the create-user form from repeated table-row forms;
- use a focused user detail/edit view as the user count grows.

## Global Search

Search supports:

- Acumatica invoice number/internal ID;
- Shoprite PO number;
- candidate/operation/attempt/run ID;
- customer account;
- delivery GLN/location;
- GTIN/item ID;
- correlation/message ID.

Results are grouped by entity type and permission-filtered. Search never returns
secret values or unredacted payload content.

## Operational Data Model

The console requires persisted read models in addition to raw telemetry:

- `integration_runs`;
- `integration_run_items` or linked entity references;
- `integration_exceptions`;
- `integration_exception_comments`;
- `integration_event_inbox`;
- `integration_outbox`;
- `submission_operations`;
- `automation_policy_versions`;
- `connection_health_snapshots`;
- existing candidates, attempts, mappings, audit events, users and roles.

Exception deduplication key:

```text
environment + entity type + entity ID + error code + active state
```

Run records store:

- run type;
- trigger/actor;
- environment;
- correlation ID;
- status;
- configuration version;
- started/completed;
- cursor before/after;
- counts;
- error summary;
- trace link.

## API and Command Contracts

### Read APIs

Required read surfaces:

```text
GET /api/operations/summary
GET /api/invoices/candidates
GET /api/invoices/candidates/{id}
GET /api/shoprite/purchase-orders
GET /api/shoprite/purchase-orders/{id}
GET /api/exceptions
GET /api/exceptions/{id}
GET /api/integration-runs
GET /api/integration-runs/{id}
GET /api/mappings/{type}
GET /api/automation/policy
GET /api/connections
GET /api/audit-events
GET /api/admin/users
```

List endpoints require:

- server-side filters;
- stable sorting;
- cursor pagination;
- total/filtered counts where affordable;
- explicit freshness timestamp;
- permission-aware payload redaction.

### Command APIs

Commands include:

- refresh Shoprite POs;
- reconcile Acumatica invoices;
- revalidate invoice;
- submit invoice;
- hold/release invoice;
- save/verify mapping;
- change automation policy;
- emergency stop/resume;
- claim/comment/resolve exception;
- resolve ambiguous operation;
- retry safe operation;
- replay safe dead letter;
- test connection;
- create/update/disable user.

Every command request includes or derives:

- command ID for idempotency;
- expected entity version for optimistic concurrency;
- actor;
- initiation mode;
- environment;
- reason where required;
- correlation ID.

Long-running commands return `202 Accepted` with:

```json
{
  "commandId": "uuid",
  "runId": "uuid",
  "status": "Accepted",
  "statusUrl": "/api/integration-runs/{runId}"
}
```

The console follows the run rather than holding an HTTP request open.

### Concurrency

Mutating forms send the expected version last rendered. A stale update returns
`409 Conflict` with current state and a plain-language explanation. The UI
reloads the affected section before another action.

The UI never infers that a command succeeded because the POST returned `202`.
It shows `Accepted`, `Running`, and the persisted terminal result separately.

## Refresh and Live Updates

Initial approach:

- server-rendered first load;
- targeted client polling for status bands, active runs, and urgent counts;
- 15-second default while a page is active;
- slower polling when the tab is hidden;
- manual refresh control;
- no full-page navigation reload for command progress.

Server-Sent Events can replace polling later if measurements justify it. Do not
make WebSockets a production dependency for the first release.

Every screen displays:

- data generated/refreshed time;
- stale indicator when threshold is exceeded;
- partial-data warning when one read model is unavailable.

## Observability Model

The console combines three layers:

1. Persisted operational truth: candidates, operations, attempts, runs,
   exceptions, policy and audit.
2. Metrics: counts, latency, age, freshness and success/error rates.
3. Deep telemetry: structured logs and traces in Application Insights.

The console is authoritative for workflow state. Application Insights is the
diagnostic drill-down.

Required service-level indicators:

- PO inbox freshness;
- Acumatica event-to-discovery lag;
- reconciliation freshness;
- candidate-ready-to-queued latency;
- queued-to-submitted latency;
- submission success/rejection/ambiguity rate;
- oldest queue message;
- duplicate suppression count;
- exception age;
- connector availability and latency;
- payload archive success.

Health is calculated from explicit thresholds and recent persisted results.
`Unknown` is distinct from `Healthy`.

Every run, command, message, operation, attempt, exception, log, and trace
shares correlation and causation IDs.

## Action Safety

Action hierarchy:

- one primary action per screen or task;
- routine secondary actions use outline or quiet styles;
- destructive/high-impact actions are not visually promoted;
- every action label is verb plus object;
- hidden/disabled actions include an explanation of the unmet condition.

Confirmation is proportional to risk:

- refresh/revalidate: immediate with progress;
- hold/release: reason;
- safe retry: confirmation and reason;
- mapping change: impact review and reason;
- ambiguous resolution: evidence, consequence and reason;
- dead-letter replay: state safety check, confirmation and reason;
- production enablement: affected cohort review and high-friction confirmation;
- emergency stop: clear consequence and reason.

The API, not the browser, decides whether a retry, replay, submit, or state
transition is legal.

## Visual and Interaction System

The console is a quiet operational application:

- dense but scan-friendly;
- table-first;
- restrained surfaces and shadows;
- page sections separated by spacing and rules rather than nested cards;
- status colour paired with icon and text;
- existing PVM green reserved for interactive actions;
- red, amber, green, and blue reserved for state;
- stable widths/heights for status strips, controls, and tables;
- no marketing hero, decorative graphics, gradients, or dashboard ornament.

Recommended shell changes:

- responsive left navigation;
- 64 px desktop utility header;
- constrained but wider operational content area;
- 8 px spacing scale with 4 px substeps for dense table controls;
- Lucide icons for navigation and command/status affordances;
- visible keyboard focus;
- minimum 48 px pointer/touch target;
- sentence-case labels;
- no letter-spaced uppercase body labels.

The current visual language can evolve rather than be replaced. Preserve the
neutral background, white work surfaces, dark text, green action colour, and
compact table style.

## Responsive Behavior

Desktop is the primary operational environment.

At narrower widths:

- navigation becomes a drawer;
- status band wraps into prioritized rows;
- tables keep essential columns and expose secondary fields in row detail;
- filters move into a sheet/drawer;
- actions remain adjacent to the affected entity;
- payloads use full-width scrollable viewers;
- confirmation dialogs remain usable without horizontal scrolling.

Mobile supports status checks, task triage, hold/emergency stop, and targeted
actions. Complex mapping edits and payload comparison should remain usable but
may direct the user to a larger viewport for efficiency; they are not silently
removed.

## Accessibility

WCAG 2.1 AA is the minimum:

- 4.5:1 text contrast;
- 3:1 controls, borders, focus and status icons;
- visible focus rings;
- semantic headings, landmarks, tables, lists and dialogs;
- keyboard-complete navigation and commands;
- no colour-only status;
- screen-reader announcements for accepted/running/completed commands;
- focus moves to validation/error summary after failed form submission;
- dialogs trap and restore focus;
- dates expose machine-readable values and clear timezone.

Store timestamps in UTC. Display Africa/Johannesburg time by default with UTC
available in detail/tooltip.

## Security and Privacy

- Enforce roles in .NET endpoints and read models.
- Next.js visibility is convenience, never authorization.
- Redact Shoprite query credentials everywhere.
- Never expose Key Vault secret values.
- Redact payload fields according to role and data classification.
- Use signed/short-lived blob access through the API, not public blob URLs.
- Audit payload download and export.
- Protect CSV export against formula injection.
- Rate-limit search, payload, export, connection test and command endpoints.
- Add CSRF protection to mutating web flows where framework defaults are not
  sufficient.
- Show environment and tenant on every control confirmation.

## Error and Empty States

Every screen must define:

- loading;
- no data;
- no filtered results;
- stale data;
- partial dependency failure;
- command accepted/running;
- command completed;
- command failed;
- unauthorized;
- optimistic-concurrency conflict;
- very large result set;
- long identifiers/error messages.

Error copy states:

1. what failed;
2. whether any external side effect may have happened;
3. the current persisted state;
4. the safe next action;
5. correlation/run ID.

Do not show generic production Server Component errors as the final operator
experience. Route errors must render an operational error boundary with retry
and correlation information.

## Implementation Slices

### Console Slice A: Shell and read-only control room

Depends on:

- production automation data model decisions;
- operational summary read model.

Deliver:

- responsive navigation and environment/mode header;
- role-aware navigation;
- control room status band;
- urgent work and health tables;
- consistent loading, empty, stale and error states;
- global design tokens and accessible status components.

Acceptance:

- Viewer can identify environment, mode, health, last successful syncs, urgent
  counts, and stale data without Azure access;
- no control-room status is inferred from colour alone;
- narrow and desktop layouts have no overlap or clipped actions.

### Console Slice B: Production invoice operations

Depends on:

- concurrency-safe submission operations;
- integration runs;
- candidate filters/read model.

Deliver:

- paginated/filterable invoice queue;
- expanded invoice detail;
- state timeline;
- source/PO/financial reconciliation;
- operation/attempt history;
- contextual payload and trace views;
- hold, revalidate and manual submit commands.

Acceptance:

- every action reflects backend eligibility;
- submitted/ambiguous candidates cannot be resent;
- accepted/running/terminal command states are distinguishable;
- existing QA workflows remain functional.

### Console Slice C: Exceptions, tasks and dead letters

Depends on:

- persisted exceptions/tasks;
- Service Bus worker and DLQ metadata;
- ambiguous-resolution backend.

Deliver:

- unified exception queues;
- task ownership/comments;
- root-cause/fix-location guidance;
- ambiguous evidence workflow;
- safe retry and DLQ replay controls;
- overdue indicators and alerts.

Acceptance:

- an Admin can resolve an ambiguous outcome without database access;
- unsafe replay is unavailable and explained;
- repeated failures deduplicate to one active task;
- all resolutions are audited.

### Console Slice D: Runs and observability

Depends on:

- persisted run lifecycle;
- correlation IDs;
- health snapshots and metrics.

Deliver:

- run list/detail;
- cursor and count visibility;
- connection/pipeline health;
- Application Insights trace links;
- freshness/latency/error metrics;
- action progress polling.

Acceptance:

- an Operator can determine when each integration last succeeded;
- every failed run links to affected records and exceptions;
- `Unknown`, stale and failed states are distinct.

### Console Slice E: Global mappings

Status as of 2026-08-12: item/GTIN and UOM mapping is implemented as reusable
inventory configuration outside finalized invoices. During invoice discovery,
an exact unique GTIN and matching supported UOM from the Acumatica inventory
item and Shoprite PO automatically creates the verified global baseline. Admin
writes are audited with a required reason and synchronously revalidate active
affected candidates. Admin intervention is reserved for missing, ambiguous or
conflicting product data; automatic resolution never overwrites a verified
mapping or infers a quantity conversion. Location/GLN, pack, tax, pagination,
and asynchronous revalidation remain.

The mapping console also derives an exception queue directly from the complete
refreshed Shoprite PO catalogue. A buyer item with no verified global mapping
appears once regardless of how many POs contain it. Admin can resolve that
exception or preconfigure any known Shoprite buyer item by entering an exact
Acumatica SKU, validating the SKU and its available UOMs live against the
Acumatica Stock Item endpoint, selecting the Shoprite UOM, and recording a
mandatory reason. Correcting a conflict atomically reassigns the global buyer
item/SKU relationship and audits the displaced mapping instead of leaving an
ambiguous duplicate.

Acumatica and Shoprite may use different identifiers for the same product. In
that case the initial global assignment is explicitly verified from Acumatica
inventory and the complete available Shoprite PO catalogue, then reused
automatically. The product mapping identifies the stable Shoprite buyer item;
the matched PO line remains authoritative for the GTIN sent on that invoice.
As of 2026-08-12, all 10 Shoprite buyer items observed across 202 QA POs are
mapped exactly once to Acumatica inventory, covering 17 historical/current GTIN
variants with no unmapped or ambiguous products. All use Acumatica UOM `BOX`
and Shoprite UOM `EA`.

Depends on:

- mapping APIs;
- mapping version/audit;
- affected-candidate revalidation command.

Deliver:

- item/GTIN, UOM, location/GLN and tax mapping views;
- search/filter/pagination;
- verification state;
- impact preview;
- versioned edits and asynchronous revalidation.

Acceptance:

- Admin can manage reusable mappings outside a single invoice;
- production-unverified mappings remain blocking;
- financial source values cannot be edited.

### Console Slice F: Automation and connections

Depends on:

- persisted automation policy;
- emergency-stop command;
- allowlist/schedule configuration;
- connector health/test commands.

Deliver:

- automation mode and policy;
- allowlists and schedules;
- canary caps and freshness thresholds;
- emergency stop;
- connection readiness and tests;
- policy version/history.

Acceptance:

- `Disabled` and `Shadow` cannot submit automatically;
- authorized manual submission remains available unless emergency stop is
  active;
- emergency stop prevents new claims immediately;
- production enablement requires impact preview, reason and high-friction
  confirmation;
- no secret value is exposed.

### Console Slice G: Audit, users and release hardening

Depends on:

- complete command/state audit;
- payload/export authorization.

Deliver:

- audit explorer;
- correlation navigation;
- payload/export audit;
- user-management improvements;
- final accessibility, responsive, performance and failure-state pass.

Acceptance:

- every manual and automatic state/configuration change is discoverable;
- final Admin cannot be disabled;
- accessibility and authorization tests pass;
- Playwright screenshots verify desktop and narrow operational workflows.

## Integration with Backend Automation Slices

| Backend automation slice | Console dependency/output |
| --- | --- |
| 1. Persistence and submission safety | Invoice operation state, timeline and action eligibility |
| 2. Payload archive and audit | Payload viewer, hashes and audit links |
| 3. Service Bus and worker | Queue/DLQ/run visibility |
| 4. Scheduled PO refresh | PO freshness and refresh runs |
| 5. Acumatica reconciliation | Cursor, freshness and reconciliation runs |
| 6. Push-notification ingestion | Webhook health and event lag |
| 7. Automation policy | Mode, allowlist, shadow and emergency controls |
| 8. Exception operations | Exception center, ambiguity and safe retry |
| 9. Production infrastructure | Connection health, environment and telemetry links |
| 10. Shadow/canary/go-live | Control room, canary monitoring and approval evidence |

The read-only shell and control room can start once the operational read models
are stable. Mutating controls must not be built against placeholder endpoints.

## Verification

Frontend:

```powershell
npm --prefix frontend/workbench run lint
npm --prefix frontend/workbench run build
```

Required tests:

- role-based route/action tests;
- read-model rendering tests;
- command accepted/running/terminal state tests;
- optimistic-concurrency conflict tests;
- status threshold tests;
- payload redaction tests;
- safe/unsafe retry visibility tests;
- emergency-stop confirmation tests;
- final-Admin protection tests;
- accessibility checks;
- Playwright desktop and narrow viewport screenshots;
- long identifiers, errors and empty/stale/partial states.

Backend:

- endpoint authorization;
- cursor/filter contracts;
- command idempotency;
- reason/audit requirements;
- exception deduplication;
- automation policy versioning;
- connection test isolation;
- payload access authorization.

## Production Console Go-Live Gates

- Control room uses persisted operational truth.
- Environment and automation mode are visible on every page.
- Admin has full controlled access.
- Viewer and Operator permissions are verified server-side.
- Invoice, PO, exception, run, mapping, connection, audit and user workflows are
  functional.
- Ambiguous resolution and safe retry are operational.
- Queue and dead-letter state are visible.
- Emergency stop is tested.
- No action bypasses the shared command/state machine.
- Payloads and logs are redacted.
- Every mutation records actor, reason, command ID and state change.
- Stale/partial/error states are clear.
- Accessibility and responsive checks pass.
- Console remains usable during one connector outage.
- Shadow and canary operations can be monitored without Azure Portal access.

## Explicit Non-Goals

- Editing Acumatica invoice financial data.
- Editing generated Shoprite XML.
- General-purpose log analytics replacement.
- Azure infrastructure administration.
- Bulk invoice submission.
- Production BI/reporting dashboards.
- Shoprite PO-to-Acumatica sales-order automation.

Reporting will later share authentication, navigation patterns, and design
tokens, but operational control and analytical reporting remain separate
product areas.

## Recommended Sequence

1. Complete production automation Slice 1 first.
2. Define persisted run, exception, policy and health read models.
3. Build Console Slice A as a read-only operational control room.
4. Build Console Slice B with the safe submission-operation model.
5. Add exception, run, mapping and automation controls as their backend slices
   become real.
6. Make Console Slice G and its go-live gates mandatory before production
   automation moves beyond `Shadow`.
