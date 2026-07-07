# Current Project Status

Last updated: 2026-07-07

## Overall Status

Shoprite PO inbox implementation is in progress on `feature/shoprite-po-inbox`.

Azure infrastructure and access are unblocked. Shoprite QA `VendorOrder` credentials have been verified and the backend now has a local PO inbox implementation ready for QA deployment testing.

## Active Priority

Verify and deploy Shoprite `VendorOrder` PO inbox ingestion, then wire Acumatica staging invoice refresh.

Do not switch invoice submission to the real Shoprite QA `VendorInvoice` client until the PO inbox is deployed, invoice candidates match exactly one local PO, and the required Shoprite invoice endpoint credentials/headers are confirmed.

## Why This Is Next

The current design says the Shoprite PO is the pivot between Acumatica and Shoprite:

- Acumatica finalized invoice provides invoice truth.
- Shoprite PO provides delivery location, buyer/store/DC context, order-line context, and Shoprite item/GTIN context.
- Invoice candidates must match exactly one local PO before submission.

## Current Implementation State

Done:

- Shoprite `VendorOrder` HTTP client.
- Shoprite VendorOrder JSON parser for PO headers, delivery location, and lines.
- PO inbox persistence for headers, lines, raw order JSON, hashes, first/last seen timestamps.
- `POST /api/shoprite/purchase-orders/refresh`.
- `GET /api/shoprite/purchase-orders`.
- `GET /api/shoprite/purchase-orders/{id}`.
- Workbench PO inbox list/detail screens.
- Invoice candidate PO matching against the local PO inbox.
- PO-derived supplier/delivery GLN enrichment before validation.
- Validation blocking for invoice candidates whose PO is missing from the local inbox.
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

Not done:

- Real Acumatica staging invoice refresh.
- Real Shoprite QA `VendorInvoice` submission through the API runtime path.
- Blob payload archive.
- Mapping/admin pages for GLN, GTIN, UOM, pack, tax, and connection settings.
- Manual ambiguous-resolution actions.

## Canonical Handoff

Read:

- `docs/handovers/2026-06-10-shoprite-project-handoff.md`
- `AGENTS.md`

## Verification Snapshot

Most recent verification on 2026-07-07:

- `docker run ... mcr.microsoft.com/dotnet/sdk:10.0 dotnet test backend/Pvm.sln`: passed, 52 tests.
- `npm --prefix frontend/workbench run lint`: passed.
- `npm --prefix frontend/workbench run build`: passed.
- Local API smoke with Shoprite QA `VendorOrder`: imported 40 POs, persisted 40.
- Local workbench smoke: `/purchase-orders` rendered the imported PO data.
- Local invoice refresh smoke: fixture invoice `INV342699282` is blocked with `missing-local-shoprite-po` because fixture PO `PO4500123456` is not in the current Shoprite QA PO inbox.
- QA deployment path now passes Shoprite settings from Key Vault into the API and sets QA Container Apps `minReplicas=1` for UAT readiness.
- `npm ci` reported 6 npm audit findings in the frontend dependency tree: 1 low, 5 moderate.

The local host still does not have the .NET SDK installed; backend verification uses the SDK container with Docker socket access.
