# Current Project Status

Last updated: 2026-08-12

## Overall Status

Shoprite PO refresh, real Acumatica QA invoice ingestion, matching, mapping,
XML generation, and real Shoprite QA submission are deployed and verified.
Live finalized invoice `INV158888` was imported, matched exactly once to
Shoprite PO `1212021109`, reconciled to Acumatica totals, mapped, and submitted.
On 2026-07-27 Shoprite confirmed that the submitted invoice was structurally
sound, correct, and verified.

The QA payload-contract milestone is complete for the tested normal-order
scenario. Production automation Slices 1 through 6 are merged and deployed in
QA, and the Acumatica QA Generic Inquiry and push notification are active.
Global Shoprite inventory mappings and the Admin exception workflow are also
merged. Slice 7 is implemented on its feature branch but is not deployed. The
project is not ready to enable automatic production submissions.

## Infrastructure Subscription (moved 2026-07-14)

The QA estate was migrated off the pay-as-you-go subscription `PVM-01`
(`51497af4-8223-42c4-a2ef-f6f625094d2f`) onto the CSP subscription
`Azure subscription 1` (`1d0e7292-24e5-425e-870b-c56904b70da6`) so cost bills
through the Westcon CSP partner. The old resource group was deleted and a fresh
Bicep deploy recreated the estate under the same names, except the Key Vault,
now `kv-pvm-intg-qa` (the old vault's purge protection reserves its name for 30
days). The new Container Apps environment domain suffix is `blackbay-85d5b3d6`,
so the workbench/API FQDNs and the workbench Entra redirect URI changed
accordingly. GitHub `AZURE_SUBSCRIPTION_ID` and the deployer service principal's
role assignments were repointed to the new subscription.

Live estate check on 2026-08-07:

- `origin/main` is merge commit `676a75cf` for incremental reconciliation PR #19.
- QA deployment run `31154922607` completed successfully, including backend and
  frontend verification, migrations, image publication, Bicep deployment, and
  smoke checks using image tag `qa-676a75cfb471`.
- API `/health` returns `200`; anonymous invoice API returns `401`; unauthenticated
  workbench `/invoices` redirects to sign-in.
- The only PVM resource group is `rg-pvm-integrations-qa`; no production group
  has been provisioned.
- Service Bus queues are provisioned and had zero active/dead-letter messages
  after the controlled scheduler test.
- Blob versioning and seven-day blob/container soft deletion are enabled.
- The API managed identity has `Storage Blob Data Contributor` on the archive
  account.
- Seeded candidate `QA-INV-1212503708` created four archive objects with one
  current version each. Independent downloads matched every PostgreSQL SHA-256
  hash and byte count.

## Active Priority

Review, merge, deploy, and runtime-verify Slice 7 of
`docs/implementation-plans/shoprite-production-automation-plan.md`: persisted
automation policy and decisions, `Shadow` evaluation, allowlists, freshness and
stabilization controls, audited emergency stop, and the Automation Control
console. Deployment must retain the seeded `Disabled` policy.

## Why This Is Next

The current design says the Shoprite PO is the pivot between Acumatica and Shoprite:

- Acumatica finalized invoice provides invoice truth.
- Shoprite PO provides delivery location, buyer/store/DC context, order-line context, and Shoprite item/GTIN context.
- Invoice candidates must match exactly one local PO before submission.

## Current Implementation State

Done:

- Service Bus queues, transactional outbox dispatcher, worker consumers, and
  dead-letter operational views deployed and verified in QA.

- Shoprite `VendorOrder` HTTP client.
- Shoprite VendorOrder JSON parser for PO headers, delivery location, and lines.
- PO inbox persistence for headers, lines, raw order JSON, hashes, first/last seen timestamps.
- `POST /api/shoprite/purchase-orders/refresh`.
- `GET /api/shoprite/purchase-orders`.
- `GET /api/shoprite/purchase-orders/{id}`.
- Workbench PO inbox list/detail screens.
- PO-detail action to seed a deterministic QA invoice candidate from a selected Shoprite PO.
- Invoice candidate PO matching against the local PO inbox.
- PO-derived supplier/delivery GLN enrichment before validation.
- Validation blocking for invoice candidates whose PO is missing from the local inbox.
- Runtime guard that blocks submission unless the candidate is matched to a local Shoprite PO.
- Real Shoprite QA `VendorInvoice` client can be selected with `Shoprite__InvoiceSubmissionMode=RealQa`.
- Backend build gate fixed by pinning patched `Microsoft.OpenApi`.
- Fixture-backed invoice candidate refresh.
- Canonical invoice model and validation.
- Shoprite invoice XML generation.
- Submission command path.
- Duplicate and ambiguous attempt handling.
- Persistence for invoice candidates and attempts.
- Local stub submission.
- Workbench invoice list/detail.
- Microsoft Entra workbench auth and app-managed user authorization.
- Azure QA baseline.
- Acumatica contract REST session client with explicit login/logout.
- Finalized `SalesInvoice` account filtering and bounded pagination.
- Acumatica invoice/detail mapping into the existing source DTO.
- Idempotent invoice-candidate upsert with local Shoprite PO matching.
- PO-derived supplier GLN, delivery GLN, and GTIN enrichment.
- Safe `Fixture` versus `RealQa` invoice-source switch.
- Live Acumatica QA authentication for tenant `PVM` and endpoint
  `Default/24.200.001`.
- Live `SalesInvoice`, `SalesInvoiceDetail`, `SalesInvoiceTaxDetail`, and
  `Customer` contract verification.
- Parent-account expansion from `DEB2062` to invoice-level store/DC customer
  accounts, including paged customer resolution and bounded invoice-filter chunks.
- Invoice-only filtering, mandatory invoice-date cutover, and exclusion of credit memos.
- Per-record invoice detail/tax retrieval compatible with Acumatica's BQL-delegate restriction.
- Discount-aware line and VAT allocation that reconciles to Acumatica invoice totals.
- Live Acumatica invoice `INV158888` imported and matched to Shoprite PO
  `1212021109`.
- Live Acumatica invoice `INV158888` mapped, submitted to Shoprite QA, and
  confirmed by Shoprite as structurally sound and correct.
- Persistent, verified item/GTIN mappings keyed by Acumatica inventory ID and
  Shoprite buyer item ID.
- Persistent, verified UOM mappings keyed by Acumatica inventory ID and
  Acumatica UOM.
- Admin-only invoice-line mapping action using an explicitly selected Shoprite
  PO line and Shoprite UOM.
- Mapping audit events and immediate candidate revalidation.

- Shared PO matching, mapping enrichment, and validation path for refresh and
  manual revalidation.
- Explicit EF migration deployment with runtime schema creation removed.
- Persisted submission-operation state machine and database concurrency guards.
- Frozen source, canonical, request, and payload-hash versions per operation.
- At-most-one external POST under concurrent/redelivered submission commands.
- Stale in-flight sends become `Ambiguous` rather than being retried.
- Azure Blob archive for source, canonical, request, and response payloads.
- SHA-256 archive verification and write-once Blob behavior.
- Immutable database transition audit with actor, mode, correlation, reason,
  source version, payload hash, and timestamp.
- Raw archived bodies removed from terminal PostgreSQL operation/attempt rows.
- Deployed seeded QA candidate `QA-INV-1212503708` returned HTTP `200`, created
  four verified blobs, and transitioned `Pending -> Sending -> Submitted`.
- Five-minute Azure Container Apps scheduled job that writes a durable PO
  refresh command through the shared outbox path.
- Persisted integration-run lifecycle and counts for manual and scheduled PO
  refreshes.
- Idempotent scheduled windows and unchanged-payload handling without line-row
  churn or duplication.
- Changed-PO candidate revalidation.
- Control Room, run history/detail, and PO freshness views.
- Fifteen-minute stale threshold and Azure Monitor alert.
- Controlled Slice 4 fault test proved stale status, automatic-processing
  blocking, alert firing and action-group delivery, recovery, alert resolution,
  and empty active/dead-letter queues. PO refresh recovered with 186 POs and two
  matched candidates.
- Incremental Acumatica reconciliation with persisted cursors, overlap, daily
  lookback, current-source verification, Admin visibility, and a 30-minute stale
  alert.
- Slice 5 runtime verification completed a bootstrap run, daily lookback, and two
  consecutive scheduled incremental windows. Each durable command published and
  completed once, candidate identity remained stable, submission operations did
  not increase, Service Bus active/dead-letter counts returned to zero, and the
  stale alert fired before bootstrap then resolved after recovery.

Implemented and merged since the previous status snapshot:

- Bounded authenticated Acumatica webhook endpoint with company/query allowlists,
  a 64 KiB body limit, and per-source rate limiting.
- PostgreSQL event inbox and transactional outbox deduplicated by environment,
  company, query, and Acumatica transaction ID.
- One per-invoice discovery command per unique event row, followed by an
  authoritative Contract REST fetch and normal candidate validation path.
- Blocking treatment when an existing candidate is no longer finalized.
- Admin Control Room health plus an Admin-only webhook event inbox view that
  exposes metadata and hashes but not raw payloads or secrets.
- Bicep, Key Vault deployment wiring, CI smoke check, and the Acumatica Generic
  Inquiry/push destination/recovery runbook.
- Global inventory mapping bootstrap from known Shoprite PO items.
- Admin mapping list/create/edit workflow and automatically seeded unresolved
  mapping exceptions.

Implemented, pending review, merge, and QA deployment:

- Immutable versioned automation policy with optimistic Admin updates.
- Persisted candidate decisions with policy/source evidence and reason codes.
- `Disabled`, `Shadow`, `Allowlisted`, and `Enabled` modes, with account,
  location, and order-type controls.
- Stabilization delay, source freshness, automatic time window, and daily cap.
- Audited emergency stop that blocks both automatic and manual submission.
- Transactionally serialized policy changes and external-send claims.
- Admin Automation Control page with active policy, safeguards, decision
  summary/history, policy history, high-friction enablement, and emergency
  controls.

Not done:

- QA deployment and controlled shadow-mode verification of Slice 7.
- Remaining production admin controls beyond the Control Room/run visibility.
- Separate production infrastructure and release pipeline.
- Global mapping coverage for GLN, pack, tax, and connection settings beyond
  the implemented inventory/GTIN/UOM mapping workflow.
- Manual ambiguous-resolution actions.

## Production Automation Position

Status: **Approved and ready to implement; production sending remains
disabled.**

The approved architecture is:

- Acumatica push notification for low-latency discovery.
- Scheduled last-modified reconciliation for completeness.
- Scheduled Shoprite PO inbox refresh.
- PostgreSQL inbox/outbox and submission-operation state.
- Azure Service Bus worker commands and dead letters.
- Shared manual/automatic submission command.
- `Disabled -> Shadow -> Allowlisted -> Enabled` rollout.
- No automatic retry after an uncertain Shoprite POST.
- Functional admin control plane for health, invoices, POs, exceptions, runs,
  mappings, automation, connections, audit, users, and emergency stop.

The immediate gate is Slice 7 review, QA deployment in `Disabled`, and a
controlled `Shadow` evaluation proving visibility without creating an external
Shoprite POST. Production sending remains disabled.

The admin console is specified in
`docs/implementation-plans/integration-admin-console-plan.md`. Its read-only
control-room foundation follows the operational read-model decisions; its
mutating controls must use completed backend command paths. Production cannot
move beyond `Shadow` until the required console go-live gates pass.

## Canonical Handoff

Read:

- `docs/handovers/2026-06-10-shoprite-project-handoff.md`
- `AGENTS.md`

## Verification Snapshot

Slice 6 branch verification on 2026-08-07:

- 127 non-Azurite backend tests passed: 12 domain, 24 application, 81
  infrastructure, and 10 API. This includes notification parsing, concurrent
  inbox deduplication, migration, authoritative per-invoice refresh, and
  non-finalized blocking tests.
- The seven pre-existing Blob archive tests remain unavailable in the nested SDK
  container because Azurite returns HTTP 400; Slice 6 does not change the payload
  archive.
- A local compiled-container HTTP test returned `200` health, `401` for a missing
  secret, `202` for first receipt and exact redelivery, `400` for invalid source
  and malformed JSON, and `413` for an oversized body. PostgreSQL recorded one
  event, one duplicate, and one queued command.
- Workbench lint, 2 tests, and production build passed.
- `az bicep build --file infra/azure/main.bicep` passed.
- QA Key Vault secret `acumatica--webhooksecret` is provisioned and enabled;
  its value was not printed or stored in the repository. The Acumatica push
  destination remains inactive.
- QA deployment and Acumatica-originated runtime evidence remain pending merge.

Slice 5 verification completed on 2026-08-07:

- Backend release build passed with zero warnings.
- 118 non-Azurite backend tests passed: 12 domain, 24 application, 77
  infrastructure, and 5 API. These include focused Acumatica, migration, cursor,
  overlap, source-version, and submission-concurrency tests.
- Seven pre-existing Blob archive tests could not run in the nested SDK
  container because Azurite returned HTTP 400; no payload-archive code changed
  in this slice.
- Workbench lint, 2 tests, and production build passed.
- `az bicep build --file infra/azure/main.bicep` passed.
- QA deployment run `31154922607` passed. Bootstrap run
  `b8fb85ca-8115-4c45-bff1-45dd03156356`, daily lookback run
  `99211d11-96e7-49d2-91de-aebbbb43fd15`, and incremental runs
  `ae363c8d-4753-40eb-90be-281ee6f06e6b` and
  `d9b22141-bf4b-4e1d-be42-ef1b7797c570` succeeded. Alert incident
  `1ffa7fce-c8ae-d80b-a8d9-400a4b77002f` fired while stale and resolved after
  recovery; automatic submission remained disabled.

Slice 4 branch verification on 2026-08-04:

- Backend: 111 tests passed (12 domain, 20 application, 74 infrastructure,
  5 API), including explicit Azurite and Testcontainers-backed checks.
- Workbench lint, 2 tests, and production build passed.
- `az bicep build --file infra/azure/main.bicep` passed.
- The production worker image built successfully; its one-shot scheduler command
  queued a persisted run/outbox message and exited cleanly without the previous
  `libgssapi` warning.
- QA deployment run `31101622980` passed. A controlled 15-minute fault test
  fired alert incident `e8752078-4b40-60db-d7f9-dcfc502e002f`, delivered action
  notifications for fire and resolution, blocked stale processing, recovered on
  execution `job-pvm-po-refresh-qa-pif7xze`, and resolved after healthy checks.

Mapping slice verification on 2026-07-24:

- Backend release build passed in the .NET 10 SDK container.
- Backend: 72 tests passed (12 domain, 16 application, 40 infrastructure,
  4 API).
- The new PostgreSQL-backed test proves an Admin mapping save persists item and
  UOM mappings, writes both audit records, and revalidates the candidate to
  `Ready`.
- Workbench lint, 2 tests, and production build passed.
- Patched Next.js from `16.2.6` to `16.2.11` and NextAuth from `4.24.14`
  to `4.24.15`, removing the direct high/critical advisories. The production
  audit still reports three high transitive findings in Next.js-pinned
  PostCSS/Sharp versions; npm offers no compatible upgrade.
- Local API/runtime smoke saved a mapping through the real HTTP endpoint and
  returned a `Ready` candidate with verified UOM and zero validation issues.
- Playwright desktop and 390 px mobile screenshots verified the mapping control
  without overlap or clipped container content.
- QA deployment run `30092563471` passed from merge commit `7e4b6c1ff5aa`.

Historical QA deployment verification on 2026-07-24:

- GitHub Actions deploy run `30092563471` passed from commit `7e4b6c1ff5aa`.
- Deployed QA images:
  - API: `acrpvmintegrationsqa.azurecr.io/pvm-api:qa-7e4b6c1ff5aa`
  - Workbench: `acrpvmintegrationsqa.azurecr.io/pvm-workbench:qa-7e4b6c1ff5aa`
- Active revisions are healthy with one replica and 100 percent traffic:
  - API: `ca-pvm-api-qa--0000004`
  - Workbench: `ca-pvm-workbench-qa--0000005`
- Live QA smoke:
  - API `/health`: `200`
  - Anonymous invoice candidates API: `401`, expected
  - Workbench `/invoices`: `200`, ending on the blackbay sign-in page
- Runtime configuration is verified:
  - `Acumatica__InvoiceSourceMode=RealQa`
  - Acumatica base URL, company `PVM`, endpoint `Default/24.200.001`, parent
    account `DEB2062`, and 2026-07-01 cutover are present.
  - Acumatica username/password and PostgreSQL connection use Container App
    secret references.
  - Workbench callback and API URLs both use the new blackbay FQDNs.
- Key Vault `kv-pvm-intg-qa` contains all 14 required deployment secrets.
- PostgreSQL contains 75 Shoprite POs and 190 PO lines. All 190 lines include a
  GTIN; none include a supplier item ID or measurement UOM.
- At this snapshot, `INV158888` was persisted with zero submission attempts and
  exactly one match to PO `1212021109`. It was mapped and submitted after this
  snapshot.

Previous verification on 2026-07-07:

- `docker run ... mcr.microsoft.com/dotnet/sdk:10.0 dotnet test backend/Pvm.sln`: passed, 52 tests.
- `npm --prefix frontend/workbench run lint`: passed.
- `npm --prefix frontend/workbench run build`: passed.
- Local API smoke with Shoprite QA `VendorOrder`: imported 40 POs, persisted 40.
- Local workbench smoke: `/purchase-orders` rendered the imported PO data.
- Local invoice refresh smoke: fixture invoice `INV342699282` is blocked with `missing-local-shoprite-po` because fixture PO `PO4500123456` is not in the current Shoprite QA PO inbox.
- QA deployment path now passes Shoprite settings from Key Vault into the API and sets QA Container Apps `minReplicas=1` for UAT readiness.
- QA deployment run `28871319424` passed on 2026-07-07 from branch `feature/shoprite-po-inbox`.
- Deployed QA images:
  - API: `acrpvmintegrationsqa.azurecr.io/pvm-api:qa-c590486f3a44`
  - Workbench: `acrpvmintegrationsqa.azurecr.io/pvm-workbench:qa-c590486f3a44`
- Live QA smoke:
  - API `/health`: `200`
  - Anonymous PO inbox API: `401`, expected
  - Workbench `/purchase-orders`: `200`
- `npm ci` reported 6 npm audit findings in the frontend dependency tree: 1 low, 5 moderate.

## QA UAT Outcome

Passed for the tested normal-order invoice path:

1. Shoprite QA POs refreshed into the durable local inbox.
2. Finalized Acumatica invoice `INV158888` refreshed from the real QA instance.
3. The captured PO reference matched exactly one Shoprite PO.
4. Item/GTIN and UOM mappings were verified.
5. Candidate validation and generated XML were reviewed.
6. The invoice was manually submitted through the real Shoprite QA
   `VendorInvoice` endpoint.
7. Shoprite confirmed that the submitted invoice was structurally sound,
   correct, and verified.

This closes the QA contract milestone for that scenario. Allocation orders,
catch-weight items, production connectivity, event delivery, concurrency,
automatic policy, and production recovery remain separate gates.

Acumatica-source connector verification completed locally on 2026-07-14:

- Login `204`, live endpoint Swagger `200`, logout `204`.
- `DEB2062` confirmed as parent account; child account resolution returns more
  than one page and includes the store/DC customer IDs represented in Shoprite POs.
- Compiled API refresh against Acumatica QA completed with
  `received=0, created=0, updated=0` for the 2026-07-01 cutover, confirming no
  accidental historical import and no current UAT invoice yet.
- A live historical invoice proved document-discount and tax-detail mapping:
  header `Amount - TaxTotal` reconciles to the taxable line total, while
  `DetailTotal` is pre-document-discount and must not be used as invoice total
  excluding VAT.

Operator note:

- CLI-authenticated smoke for protected API endpoints remains blocked by Entra
  consent for Azure CLI against the API scope; browser sign-in is the current
  operator path.

The local host still does not have the .NET SDK installed; backend verification uses the SDK container with Docker socket access.
