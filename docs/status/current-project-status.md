# Current Project Status

Last updated: 2026-07-14

## Overall Status

Shoprite PO refresh and Shoprite QA seeded invoice submission are deployed and
verified. The active implementation slice is
`feature/acumatica-qa-connector`, which replaces fixture-only invoice refresh
with an explicitly enabled Acumatica QA source.

Azure infrastructure and access are unblocked. Shoprite QA `VendorOrder` credentials have been verified and the backend now has a local PO inbox implementation ready for QA deployment testing.

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
role assignments were repointed to the new subscription. The estate was verified
live: API `/health` `200`, anonymous API `401`, workbench `/invoices` `200`.

The migration config is merged into `feature/acumatica-qa-connector`. QA deploy
run `29340894444` applied that branch at commit `a1a20c904836` to the CSP
subscription on 2026-07-14. `main` still needs the feature branch merged before
a push-triggered deployment from `main` can be treated as the canonical release
path.

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
- Live Acumatica QA authentication for tenant `PVM` and endpoint
  `Default/24.200.001`.
- Live `SalesInvoice`, `SalesInvoiceDetail`, `SalesInvoiceTaxDetail`, and
  `Customer` contract verification.
- Parent-account expansion from `DEB2062` to invoice-level store/DC customer
  accounts, including paged customer resolution and bounded invoice-filter chunks.
- Invoice-only filtering, mandatory invoice-date cutover, and exclusion of credit memos.
- Per-record invoice detail/tax retrieval compatible with Acumatica's BQL-delegate restriction.
- Discount-aware line and VAT allocation that reconciles to Acumatica invoice totals.

Not done:

- One finalized Acumatica UAT invoice dated on or after 2026-07-01 whose
  `CustomerOrder` matches a current Shoprite QA PO.
- End-to-end Acumatica-source candidate match, validation, XML review, and
  Shoprite QA submission.
- Production-grade payload archive.
- Blob payload archive.
- Mapping/admin pages for GLN, GTIN, UOM, pack, tax, and connection settings.
- Manual ambiguous-resolution actions.

## Canonical Handoff

Read:

- `docs/handovers/2026-06-10-shoprite-project-handoff.md`
- `AGENTS.md`

## Verification Snapshot

Most recent deployment verification on 2026-07-14:

- GitHub Actions deploy run `29340894444` passed from commit `a1a20c904836`.
- Backend: 70 tests passed (12 domain, 16 application, 38 infrastructure, 4 API).
- Workbench: lint, 2 tests, and production build passed.
- Deployed QA images:
  - API: `acrpvmintegrationsqa.azurecr.io/pvm-api:qa-a1a20c904836`
  - Workbench: `acrpvmintegrationsqa.azurecr.io/pvm-workbench:qa-a1a20c904836`
- Active revisions are healthy with one replica and 100 percent traffic:
  - API: `ca-pvm-api-qa--0000002`
  - Workbench: `ca-pvm-workbench-qa--0000003`
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
- PostgreSQL is reachable and contains 40 Shoprite POs, 96 PO lines, one app
  user, zero invoice candidates, and zero submission attempts.
- The PO inbox `LastSeenAt` is `2026-07-14 14:06:47 UTC`, so the retained PO
  context is current for this verification.

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

Pending before Acumatica-source invoice UAT:

- Create/finalize one test invoice against a store/DC child customer and set its
  customer order reference to a current Shoprite QA PO number.
- Sign in to the blackbay workbench and run `Refresh queue`; the expected result
  before that invoice exists is zero candidates.
- After the invoice exists, confirm one candidate, exactly one local PO match,
  reconciled totals, no blocking validation issues, and the generated XML before
  the first manual Shoprite QA submission.
- CLI-authenticated smoke for protected API endpoints is blocked by Entra
  consent for Azure CLI against the API scope; browser sign-in remains the
  correct operator path.

The local host still does not have the .NET SDK installed; backend verification uses the SDK container with Docker socket access.
