# Azure Container Apps Deployment Notes

These notes describe the intended hosted shape for the Shoprite invoice upload MVP. They are not a production deployment script yet.

## Target Shape

Hosted components:

- API container: ASP.NET Core integration API
- Workbench container: Next.js admin workbench
- Worker container: future invoice refresh/submission/background processors
- PostgreSQL: Azure Database for PostgreSQL Flexible Server
- Queue: Azure Service Bus
- Payload archive: Azure Blob Storage
- Secrets: Azure Key Vault or Container Apps managed secrets
- Observability: Azure Monitor / Application Insights with OpenTelemetry

## Container Apps

Recommended apps:

| App | Purpose | Public ingress |
| --- | --- | --- |
| `pvm-api` | Integration API and workbench backend | Internal first; public only behind auth/proxy |
| `pvm-workbench` | Admin workbench UI | Public or private, depending on identity/network stance |
| `pvm-worker` | Scheduled refresh, queue consumers, retries | No |

The current repo has API and workbench projects. The worker is future scope.

## API Container

Runtime:

- .NET 10
- ASP.NET Core
- PostgreSQL connection
- Shoprite and Acumatica connector configuration

Required environment:

```text
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__Pvm=<postgres-connection-string>
Shoprite__BaseUrl=<shoprite-qa-or-prod-base-url>
Shoprite__Username=<secret>
Shoprite__Password=<secret>
Shoprite__ContractId=<contract-id>
Shoprite__UiUser=<ui-user>
Acumatica__BaseUrl=<acumatica-base-url>
Acumatica__Tenant=<tenant>
Acumatica__Branch=<branch>
Acumatica__Username=<secret>
Acumatica__Password=<secret>
Acumatica__EndpointName=<endpoint>
Acumatica__EndpointVersion=<version>
BlobStorage__PayloadContainer=<container-name>
ServiceBus__ConnectionString=<secret-or-managed-identity-config>
```

Readiness:

- expose `/health`
- add dependency health checks for PostgreSQL, Service Bus, Blob Storage, Acumatica, and Shoprite before production
- keep Shoprite and Acumatica external checks separate from basic container liveness

## Workbench Container

Runtime:

- Next.js
- Node.js runtime compatible with the lockfile
- API URL available to server actions and server-rendered pages

Required environment:

```text
NEXT_PUBLIC_API_BASE_URL=https://<api-host>
```

Before production:

- add authentication
- add role-based authorization for viewer, submitter, and admin
- ensure admin-only data is not exposed to unauthenticated users
- configure HTTPS-only ingress

## PostgreSQL

Recommended service:

- Azure Database for PostgreSQL Flexible Server

Configuration:

- private networking where practical
- backups enabled
- point-in-time restore enabled
- connection pooling strategy decided before production load
- migration strategy defined before schema changes become frequent

Current schema stores:

- invoice candidates
- submission attempts
- audit events
- canonical/source/validation JSON snapshots

Production hardening should add:

- explicit EF migrations
- payload-location fields backed by Blob Storage
- additional uniqueness constraints for business idempotency keys
- operational indexes for status, updated time, and queue views

## Azure Service Bus

Use Service Bus for:

- scheduled candidate refresh requests
- future invoice-finalized events
- retryable submission commands
- dead-letter queues for poison messages

Recommended conventions:

- message IDs derived from business idempotency keys
- duplicate detection enabled on critical queues/topics
- dead-letter reason includes validation or connector failure category
- all queue consumers use persisted state before external side effects

MVP manual submission can run directly through the API command path. Future automatic submission should reuse the same command handler behind queue consumers.

## Blob Storage

Use Blob Storage for:

- raw Acumatica payload snapshots
- generated Shoprite XML payloads
- Shoprite response bodies
- larger diagnostic payloads

Recommended path pattern:

```text
payloads/shoprite/invoices/{yyyy}/{MM}/{invoiceCandidateId}/{attemptId}/request.xml
payloads/shoprite/invoices/{yyyy}/{MM}/{invoiceCandidateId}/{attemptId}/response.xml
payloads/acumatica/invoices/{yyyy}/{MM}/{invoiceCandidateId}/source.json
```

Store hashes and blob locations in PostgreSQL. Do not store credentials or sensitive headers in blobs.

## Secrets

Use Key Vault or Container Apps secrets for:

- PostgreSQL connection string
- Shoprite username/password
- Acumatica username/password or OAuth secret
- Service Bus credentials if managed identity is not used
- Blob Storage credentials if managed identity is not used

Prefer managed identity where possible.

Secret rules:

- no secrets in repo
- no secrets in logs
- no secrets in payload archive
- rotate Shoprite and Acumatica credentials before production launch

## Networking

Outbound dependencies:

- Acumatica tenant host
- Shoprite Auto Download API host
- Azure PostgreSQL
- Azure Service Bus
- Azure Blob Storage
- Azure Key Vault
- monitoring endpoints

Shoprite guidance from the REST guide favors URL allowlisting where possible because server/IP moves can occur. If firewall rules are required, prefer host allowlists over brittle IP allowlists when the network team permits it.

Inbound:

- workbench should require authentication
- API should be private to the workbench/worker where possible
- if public API ingress is required, use identity, TLS, rate limits, and IP restrictions

## Observability

Emit structured logs for:

- candidate refresh
- validation outcomes
- submission attempts
- Shoprite response status
- ambiguous outcomes
- duplicate blocks
- manual actions

Metrics:

- candidate count by status
- submissions by status
- retry count
- ambiguous count
- oldest unresolved failure age
- Shoprite latency
- Acumatica latency

Tracing:

- one trace per refresh, validation, and submission command
- include candidate ID and idempotency key as safe attributes
- never include XML payload bodies or credentials in trace attributes

## Promotion Path

1. Local fixture QA:
   - sanitized fixture
   - local Shoprite stub
   - local PostgreSQL

2. Acumatica staging read:
   - real staging invoice extraction
   - still use Shoprite stub
   - validate mapping assumptions

3. Shoprite QA submit:
   - real Shoprite QA `VendorInvoice`
   - one manually selected invoice
   - compare Shoprite portal/API test tool outcome

4. Production readiness:
   - auth and roles
   - explicit migrations
   - blob payload archive
   - managed secrets
   - full audit trail
   - ambiguous-resolution admin action
   - production runbook and rollback plan

## Current Repo Gaps

- no container Dockerfiles yet
- no CI/CD pipeline yet
- no EF migration files yet
- no managed identity or Key Vault wiring yet
- no Service Bus worker yet
- no Blob Storage payload archive yet
- no workbench authentication yet
