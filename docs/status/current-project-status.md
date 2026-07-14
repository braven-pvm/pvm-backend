# Current Project Status

Last updated: 2026-07-14

## Overall Status

Shoprite PO refresh and Shoprite QA seeded invoice submission are deployed and
verified. The active implementation slice is
`feature/acumatica-qa-connector`, which replaces fixture-only invoice refresh
with an explicitly enabled Acumatica QA source.

Azure infrastructure and access are unblocked. Shoprite QA `VendorOrder` credentials have been verified and the backend now has a local PO inbox implementation ready for QA deployment testing.

## Active Priority

Connect the supplied Acumatica QA instance, verify its endpoint schema, and
refresh finalized Shoprite-account invoices into the existing PO-pivoted
candidate workflow.

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

Not done:

- Live Acumatica authentication and endpoint-schema verification.
- Confirmation of the exact Acumatica company, branch, Shoprite customer account IDs,
  and invoice line/tax/barcode fields.
- Azure Key Vault and Container App values for the Acumatica connector.
- Production-grade payload archive.
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
- QA deployment run `28871319424` passed on 2026-07-07 from branch `feature/shoprite-po-inbox`.
- Deployed QA images:
  - API: `acrpvmintegrationsqa.azurecr.io/pvm-api:qa-c590486f3a44`
  - Workbench: `acrpvmintegrationsqa.azurecr.io/pvm-workbench:qa-c590486f3a44`
- Live QA smoke:
  - API `/health`: `200`
  - Anonymous PO inbox API: `401`, expected
  - Workbench `/purchase-orders`: `200`
- `npm ci` reported 6 npm audit findings in the frontend dependency tree: 1 low, 5 moderate.

## Current UAT Position

Ready for operator smoke of the PO inbox in QA:

1. Open the QA workbench.
2. Sign in with an authorized Microsoft account.
3. Open `/purchase-orders`.
4. Click `Refresh POs`.
5. Confirm the Shoprite QA `VendorOrder` batch loads.

Ready for Shoprite-side invoice-submission QA once the seeded-submit branch is deployed:

1. Open `/purchase-orders`.
2. Open a PO with usable line data.
3. Click `Seed test invoice`.
4. Review the generated invoice XML and validation.
5. Submit manually.
6. Review the attempt response and duplicate blocking behavior.

Not yet ready for Acumatica-source invoice UAT:

- Acumatica staging invoice refresh needs integration-user credentials, company,
  branch, Shoprite customer account IDs, and field confirmation from the live endpoint schema.
- CLI-authenticated smoke for protected API endpoints is blocked by Entra consent for Azure CLI against the API scope; browser sign-in remains the correct operator path.

The local host still does not have the .NET SDK installed; backend verification uses the SDK container with Docker socket access.
