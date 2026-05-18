# Architecture and Tech Stack Options

Last researched: 2026-05-18

## Context

This system is a long-running service layer between Acumatica and trading partners such as Shoprite.

Current concrete scope:

- Submit invoices from Acumatica to Shoprite.
- Potentially ingest Shoprite purchase orders and create Acumatica sales orders.
- Later add broader Acumatica reporting, inventory, and customer/account invoice access.

Non-negotiable engineering properties:

- Idempotent: duplicate events, retries, or operator replays must not create duplicate Shoprite invoices or Acumatica sales orders.
- Reliable: external API failures, service restarts, and network timeouts must be recoverable.
- Robust: payloads must be validated, auditable, replayable, and supportable.
- Future-proof: Acumatica and Shoprite specifics should be connector modules, not hard-coded throughout the system.

## Recommended Baseline

Recommended starting stack:

- Runtime: .NET 10 LTS.
- Service style: ASP.NET Core API plus .NET Worker Services.
- Database: PostgreSQL.
- Messaging/work queue: Azure Service Bus if Azure-hosted; RabbitMQ if self-hosted.
- Messaging library: MassTransit.
- Workflow model: explicit database-backed document state machine first; consider Temporal if workflow complexity grows.
- Storage: object/blob storage for raw Shoprite and Acumatica payload snapshots.
- Admin console: Next.js or React SPA backed by the integration API.
- Reporting frontend: Next.js dashboard application sharing auth and design system with the admin console.
- Analytics store: start with PostgreSQL reporting schemas/materialized views; add ClickHouse or a lakehouse/Parquet layer when reporting volume or latency requires it.
- Observability: OpenTelemetry traces/metrics/logs, exported to Grafana/Tempo/Loki/Prometheus or Azure Monitor.
- Deployment: containers from day one; Docker Compose for local/dev; Azure Container Apps, Kubernetes, or a VM/container host for production depending on operational preference.

Why this is the best starting point:

- Acumatica examples and ecosystem are heavily C#/.NET oriented.
- .NET Worker Services are a natural fit for long-running background processing.
- .NET 10 is LTS and supported through November 2028.
- PostgreSQL gives us strong relational constraints for business idempotency.
- MassTransit gives us mature queue, retry, consumer, saga, and transactional outbox patterns without forcing us into a heavy workflow platform immediately.
- The system can later adopt Temporal for selected workflows without rewriting the whole domain model.

## Core Architecture

Use a document-processing architecture:

```text
Acumatica REST/OData
        |
        v
Integration service API + workers
        |
        +-- document state tables
        +-- inbox/outbox tables
        +-- raw payload archive
        +-- validation and mapping modules
        +-- operational/admin API
        +-- reporting/warehouse pipelines
        |
        v
Shoprite REST Web Services

Admin console / reporting frontend
        |
        v
Integration API + reporting API
        |
        +-- operational store
        +-- analytics/reporting store
```

Primary modules:

- Acumatica connector: REST/OData clients, authentication, entity retrieval, sales-order creation.
- Shoprite connector: Layer 7 headers, `VendorInvoice`, `VendorOrder`, acknowledgement/reset calls.
- Mapping layer: Acumatica invoice to Shoprite invoice document; Shoprite PO to Acumatica sales order.
- Validation layer: GLN, GTIN, tax, UOM, totals, duplicate prevention, business cutoffs.
- Workflow engine: invoice submission state machine and PO ingestion state machine.
- Persistence: documents, attempts, payload hashes, idempotency keys, external references, statuses.
- Operations API/UI: search, retry, replay, cancel, refresh, manual sync, mark resolved, view payloads and errors.
- Reporting API/UI: staff, sales-rep, and management-facing reporting over curated warehouse models.

## Idempotency Design

Idempotency should be enforced at multiple layers.

Database-level:

- Unique constraints on business keys.
- `INSERT ... ON CONFLICT` for idempotent writes.
- A persisted operation table for every external side effect.

Recommended keys:

- Shoprite invoice submission: supplier GLN + store/DC GLN + Shoprite PO number + Acumatica invoice number.
- Shoprite PO ingestion: Shoprite PO number + supplier/vendor identity.
- Acumatica sales order creation: Shoprite PO number + source system.
- Shoprite order acknowledgement: Shoprite PO number + acknowledgement action.

Queue/message level:

- Deterministic message IDs derived from business keys.
- Inbox table to ignore already-processed messages.
- Outbox table to avoid publishing messages before database state commits.
- Dead-letter queues for messages that cannot be processed automatically.

External API level:

- Never assume Shoprite or Acumatica is exactly-once.
- Store every request and response attempt.
- Query local state before re-sending a Shoprite invoice.
- Make manual replays explicit and auditable.

## Option A: .NET + Postgres + MassTransit + Queue

This is the recommended default.

Stack:

- .NET 10 LTS.
- ASP.NET Core for admin/API.
- .NET Worker Services for processors.
- PostgreSQL for state.
- MassTransit over Azure Service Bus or RabbitMQ.
- EF Core or Dapper for persistence.
- OpenTelemetry for observability.

Best fit:

- Integration platform with many document flows.
- Strong audit and reconciliation requirements.
- Moderate workflow complexity.
- Team wants explicit control over data model and operations.

Strengths:

- Practical, boring, supportable stack.
- Strong fit for Acumatica's .NET-oriented API ecosystem.
- Easy to model idempotency with Postgres constraints and MassTransit outbox/inbox.
- Can run on Azure, Kubernetes, Docker, or a VM.
- Easier to hire for than Temporal-specific workflow engineering.

Weaknesses:

- We own workflow-state implementation.
- Complex long-running workflows need careful design.
- Requires discipline around outbox, retries, and status transitions.

Verdict:

- Best first build unless we know upfront that workflows will become highly complex, multi-day, and deeply asynchronous.

## Option B: Temporal-Based Workflow Platform

Stack:

- Temporal Server or Temporal Cloud.
- .NET workers or TypeScript workers.
- PostgreSQL/SQL database for business state.
- Object storage for payload archive.
- API/admin app separate from workflow workers.

Best fit:

- Complex long-running workflows.
- Multi-step processes needing durable timers, retries, compensation, and operator visibility.
- Workflows where a document may sit for hours/days across several external interactions.

Strengths:

- Durable execution is the core product, not something we hand-roll.
- Excellent fit for retryable external calls and multi-step workflows.
- Strong visibility into workflow history.
- Reduces custom orchestration code.

Weaknesses:

- More operational complexity.
- Workflow code has determinism constraints.
- Team must learn Temporal concepts well.
- Still requires business idempotency in activities and database constraints.

Verdict:

- Very attractive if Shoprite/Acumatica flows expand into multi-step fulfilment, ASN, claims, rebates, approvals, and timed retries. For invoice submission only, it may be heavier than needed.

## Option C: Azure-Native Durable Functions + Service Bus

Stack:

- Azure Functions / Durable Functions.
- Azure Service Bus.
- Azure Database for PostgreSQL or Azure SQL.
- Azure Blob Storage.
- Azure Key Vault.
- Application Insights / Azure Monitor.

Best fit:

- Azure-first infrastructure strategy.
- Low infrastructure management appetite.
- Event-driven jobs and schedules.
- Team is comfortable with Azure operational model.

Strengths:

- Managed runtime and queue infrastructure.
- Durable orchestrations support replay and retry policies.
- Azure Service Bus supports duplicate detection using `MessageId`.
- Strong managed secret and monitoring story.

Weaknesses:

- Cloud lock-in.
- Local development and debugging can be more awkward than plain services.
- Durable orchestration replay constraints still apply.
- Complex domain state should still live in our own database, not only function history.

Verdict:

- Good if the business wants an Azure-managed platform. Less portable than Option A.

## Option D: Node.js/TypeScript + NestJS + BullMQ/Redis + Postgres

Stack:

- Node.js/TypeScript.
- NestJS API/workers.
- PostgreSQL.
- Redis/BullMQ for jobs.
- OpenTelemetry.

Best fit:

- Team is strongly TypeScript-oriented.
- Fast API/admin UI development is more important than .NET ecosystem fit.

Strengths:

- Productive TypeScript stack.
- Simple worker/job model.
- Good for lightweight integrations and admin tools.

Weaknesses:

- Redis-backed job queues are not my preferred source of truth for finance-grade document exchange.
- More care needed around XML, decimal precision, and durable operations.
- Less aligned with Acumatica's C# examples and ecosystem.

Verdict:

- Viable, but not my first choice for this system's reliability and ERP-integration profile.

## Admin Console

The admin console is not optional. It is part of the reliability model because humans need controlled ways to inspect, correct, retry, and reconcile integration failures.

Recommended approach:

- Build a web admin console backed by the same integration API.
- Keep all mutations server-side and auditable.
- Treat every manual action as a command that enters the same workflow/state machine as automated work.
- Do not let the UI bypass idempotency, validation, or outbox rules.

Admin console capabilities for the first production release:

- Dashboard: current health, stuck documents, pending work, failed submissions, dead letters, last successful Acumatica/Shoprite calls.
- Invoice outbox: candidate invoices, validation status, submission status, Shoprite response, retry state.
- Shoprite PO inbox: fetched POs, parse status, Acumatica sales-order link, acknowledgement status.
- Dead letters/errors: grouped by root cause, connector, endpoint, trading partner, and retryability.
- Manual actions: refresh from Acumatica, poll Shoprite orders, resubmit invoice, retry acknowledgement, replay a failed mapping, mark resolved, cancel a document.
- Payload viewer: redacted raw request/response XML/JSON and normalized canonical model.
- Audit log: who did what, when, why, and what state changed.
- Configuration: connection health, partner GLNs, item/GTIN mappings, UOM mappings, tax mappings, feature flags.

Frontend stack options:

### Admin Option 1: Next.js Full-Stack App

Use Next.js for admin and reporting UI, with the .NET integration API as the system-of-record backend.

Strengths:

- Strong fit for dashboards, tables, forms, auth, server-rendered pages, and later reporting UI.
- Can serve both admin and staff-facing reporting from one frontend platform.
- App Router supports server-side data access patterns and interactive client components.

Weaknesses:

- Adds a TypeScript/React stack alongside .NET.
- Need clear API boundaries so Next.js does not become a second backend with business logic.

Recommendation:

- Best overall UI direction if we expect a serious staff-facing reporting product.

### Admin Option 2: React SPA + .NET API

Use Vite/React or similar SPA hosted separately, calling the .NET API.

Strengths:

- Simple mental model.
- Clear backend/frontend separation.
- Good for highly interactive admin grids.

Weaknesses:

- More client-side data-loading boilerplate.
- Less natural for mixed server-rendered reporting pages.

Recommendation:

- Good if we want a pure API-first product and do not need Next.js server features.

### Admin Option 3: .NET Razor/Blazor Admin

Use ASP.NET Core Razor Pages, MVC, or Blazor.

Strengths:

- Single-language stack.
- Lower moving parts for a utilitarian internal admin.

Weaknesses:

- Less attractive if we later want a polished, interactive reporting product.
- Smaller component ecosystem than React for data-heavy dashboard interfaces.

Recommendation:

- Good for a narrow internal operator console, weaker for the long-term reporting frontend.

## Reporting and Warehouse Architecture

Reporting should be designed as a separate read side, not queried directly from operational workflow tables.

Use two data planes:

- Operational store: exact document state, attempts, idempotency, errors, payloads, and workflow status.
- Analytics/reporting store: curated facts, dimensions, snapshots, and aggregate models for staff-facing reports.

Initial warehouse path:

1. Land Acumatica extracts and Shoprite documents into raw/staging tables.
2. Normalize into canonical operational tables.
3. Publish curated reporting tables or materialized views.
4. Build the reporting API over curated models, not raw connector DTOs.
5. Add incremental refresh using high-water marks, `LastModified` fields, events/webhooks, and reconciliation jobs.

Recommended first implementation:

- PostgreSQL for operational data and initial reporting schemas.
- Separate schemas such as `ops`, `raw`, `staging`, `mart`, and `admin`.
- Materialized views or incrementally maintained summary tables for high-use reports.
- Blob/object storage for immutable raw payload archives.

When to add an analytics engine:

- Add ClickHouse when dashboards need fast aggregation over large volumes, near-real-time slices, or many concurrent analytical users.
- Add Parquet/object-storage lakehouse patterns when long-term historical storage, cheap archival, and offline analysis become important.
- Use DuckDB primarily for local analysis, ad hoc validation, exports, or embedded analytical jobs, not as the central multi-user warehouse.

Reporting frontend capabilities:

- Role-based access for staff, sales reps, managers, and admins.
- Customer/account views.
- Invoice status and ageing.
- Shoprite invoice submission and rejection analysis.
- Sales order and PO fulfilment visibility.
- Inventory levels and availability.
- Drill-through from aggregate report to source document, payload, and Acumatica/Shoprite references.
- Export to Excel/CSV where needed.
- Saved filters and scheduled reports later.

Reporting stack options:

### Reporting Option 1: Same Next.js App, Shared Design System

Admin and reporting live in one Next.js application with role-based sections.

Strengths:

- One frontend platform.
- Shared auth, layout, data-grid components, charting, and permissions.
- Smooth transition from operational drill-through to user-facing reports.

Weaknesses:

- Must enforce strong route/permission separation.
- Needs discipline to keep admin-only operational details away from staff-facing pages.

Recommendation:

- Best long-term product direction.

### Reporting Option 2: Separate BI Tool Plus Custom Admin

Use Metabase, Power BI, Superset, or similar for reporting, while the custom app handles operations.

Strengths:

- Fast report prototyping.
- Business users can self-serve some dashboards.
- Reduces custom chart/dashboard work.

Weaknesses:

- Harder to create workflow-aware drill-through and action flows.
- Permissions, embedding, and semantic consistency can become fragmented.

Recommendation:

- Useful as a short-term or companion analytics layer, not a replacement for the operational console.

### Reporting Option 3: Separate Reporting Web App

Build a separate staff-facing reporting app from the admin console.

Strengths:

- Clean product separation.
- Easier to harden access boundaries.

Weaknesses:

- Duplicates auth, navigation, components, deployment, and design system unless managed carefully.

Recommendation:

- Consider once reporting becomes a distinct product with different release cadence or user base.

## Infrastructure Options

### Azure Container Apps

Good default if Azure is acceptable.

- Run API and workers as separate containers.
- Use Azure Service Bus, Azure Database for PostgreSQL, Blob Storage, Key Vault, and Application Insights.
- Simpler than Kubernetes for a small team.

### Kubernetes

Best when we need strong platform control or already operate a cluster.

- Good for multiple workers, scheduled jobs, scaling, and isolation.
- Kubernetes CronJobs can run scheduled reconciliation tasks.
- Operational overhead is higher.

### VM or Docker Host

Pragmatic for early stage and low volume.

- Run Docker Compose or systemd services.
- PostgreSQL and RabbitMQ can be managed or hosted separately.
- Easiest to understand, but needs disciplined backups, monitoring, patching, and deployment.

### Serverless Functions

Useful for event-driven pieces, but less ideal as the whole system if we need rich operational workflows and replay tools.

## Persistence Model

Minimum tables/entities:

- `connections`: Acumatica and Shoprite environment config references.
- `trading_partners`: supplier/vendor identity, GLNs, Shoprite account details.
- `shoprite_purchase_orders`: raw inbound PO documents and normalized headers.
- `shoprite_purchase_order_lines`: normalized PO lines.
- `acumatica_sales_order_links`: Shoprite PO to Acumatica sales order mapping.
- `invoice_outbox`: candidate Acumatica invoices to submit to Shoprite.
- `invoice_submission_attempts`: every Shoprite submission attempt and response.
- `acknowledgement_attempts`: every Shoprite PO acknowledgement attempt.
- `document_payloads`: raw request/response payload archive metadata.
- `idempotency_keys`: optional explicit key registry if not embedded per document type.
- `dead_letters` or `exceptions`: unresolved failures needing manual action.
- `manual_actions`: operator-triggered refresh, retry, replay, cancel, and resolve commands.
- `audit_events`: immutable record of automated and manual state changes.
- `report_refresh_runs`: reporting extract/transform/load run history and high-water marks.
- `report_snapshots`: optional snapshot metadata for point-in-time report views.
- `users`, `roles`, `permissions`: only if not delegated fully to an external identity provider.

Store raw payloads in blob/object storage and keep hashes/locations in Postgres.

## Validation and Mapping

Use a canonical internal model between Acumatica and Shoprite.

```text
Acumatica DTO -> Canonical Invoice -> Shoprite Invoice Payload
Shoprite Order Payload -> Canonical Purchase Order -> Acumatica SalesOrder DTO
```

Do not map Acumatica directly to Shoprite in one large function. That would make validation, tests, replays, and future partners harder.

Validation should happen before every external side effect:

- Required field validation.
- GLN/GTIN lookup validation.
- UOM conversion validation.
- Tax/category validation.
- Decimal precision and invoice total validation.
- Duplicate key validation.
- Shoprite-specific timing rule validation.

## Observability and Operations

Required from the first production release:

- Correlation ID per document.
- Search by Acumatica invoice number, Shoprite PO number, Shoprite invoice number, GTIN, GLN, and status.
- Full status timeline per document.
- Request/response capture with credential redaction.
- Retry/replay controls with reason capture.
- Dead-letter queue dashboard.
- Manual refresh/sync controls with permission checks and audit trail.
- Reporting refresh status, data freshness indicators, and high-water mark visibility.
- Metrics for success rate, failure rate, retry count, processing latency, duplicate suppression, and partner API latency.
- Alerts for repeated Shoprite failures, Acumatica auth failure, no POs fetched for abnormal windows, and stuck documents.

## Source Notes

- .NET Worker Services support `BackgroundService` / `IHostedService` for long-running workers: https://learn.microsoft.com/en-us/dotnet/core/extensions/workers
- .NET 10 is an LTS release with support to November 2028: https://learn.microsoft.com/en-us/dotnet/core/releases-and-support
- MassTransit provides transactional outbox support for storing outbound messages before broker delivery: https://masstransit.io/documentation/patterns/transactional-outbox
- Azure Service Bus duplicate detection uses `MessageId`, and Microsoft recommends business-process-derived IDs for predictable repeatability: https://learn.microsoft.com/en-us/azure/service-bus-messaging/duplicate-detection
- PostgreSQL `INSERT ... ON CONFLICT` provides database-level upsert behavior around unique constraints: https://www.postgresql.org/docs/current/static/sql-insert.html
- Azure Durable Functions support durable orchestrations and activity retry policies: https://docs.azure.cn/en-us/azure-functions/durable/durable-functions-orchestrations
- Kubernetes CronJobs support scheduled jobs and concurrency policy controls: https://kubernetes.io/docs/concepts/workloads/controllers/cron-jobs/
- OpenTelemetry for .NET supports telemetry generation and export for traces, metrics, and logs: https://opentelemetry.io/docs/languages/net/
- Next.js App Router supports server-side data fetching patterns for React applications: https://nextjs.org/docs/app/getting-started/fetching-data
- TanStack Query provides asynchronous server-state fetching, caching, and synchronization utilities for React and other frontend frameworks: https://tanstack.com/query/
- ClickHouse positions itself for real-time analytics, data warehousing, and observability workloads: https://clickhouse.com/use-cases
- DuckDB supports efficient local analytics over Parquet, JSON, and object-storage-style data sources: https://duckdb.org/
