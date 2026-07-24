# Current Project Status

Last updated: 2026-07-24

## Overall Status

Shoprite PO refresh, seeded invoice submission, and the real Acumatica QA
invoice source are deployed and verified. Live finalized invoice `INV158888`
has been imported, matched exactly once to Shoprite PO `1212021109`, and
reconciled to the Acumatica totals.

The active implementation slice is `feature/shoprite-item-uom-mappings`. It
adds the reusable item/GTIN and item/UOM mappings needed to clear the live
candidate's two remaining validation blockers without hard-coded invoice data.
Azure infrastructure and access are unblocked.

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

The migration and Acumatica connector are merged to `main`. QA deployment run
`30087501452` applied merge commit `f9bf5c84ff94` on 2026-07-24 and is the
current canonical release.

## Active Priority

Deploy the Admin-only item/GTIN and item/UOM mapping workflow, resolve
`INV158888`, review the generated XML, and perform the first real
Acumatica-source submission to Shoprite QA.

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
- Live Acumatica invoice `INV158888` imported and matched to Shoprite PO
  `1212021109`.
- Persistent, verified item/GTIN mappings keyed by Acumatica inventory ID and
  Shoprite buyer item ID.
- Persistent, verified UOM mappings keyed by Acumatica inventory ID and
  Acumatica UOM.
- Admin-only invoice-line mapping action using an explicitly selected Shoprite
  PO line and Shoprite UOM.
- Mapping audit events and immediate candidate revalidation.
- Shared PO matching, mapping enrichment, and validation path for refresh and
  manual revalidation.

Not done:

- End-to-end Acumatica-source candidate match, validation, XML review, and
  Shoprite QA submission.
- Production-grade payload archive.
- Blob payload archive.
- Global mapping list/edit pages for GLN, GTIN, UOM, pack, tax, and connection
  settings. The MVP invoice-detail mapping action is implemented.
- Manual ambiguous-resolution actions.

## Canonical Handoff

Read:

- `docs/handovers/2026-06-10-shoprite-project-handoff.md`
- `AGENTS.md`

## Verification Snapshot

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
- QA deployment remains pending for this feature branch.

Current Acumatica connector deployment verification on 2026-07-24:

- GitHub Actions deploy run `30087501452` passed from commit `f9bf5c84ff94`.
- Deployed QA images:
  - API: `acrpvmintegrationsqa.azurecr.io/pvm-api:qa-f9bf5c84ff94`
  - Workbench: `acrpvmintegrationsqa.azurecr.io/pvm-workbench:qa-f9bf5c84ff94`
- Active revisions are healthy with one replica and 100 percent traffic:
  - API: `ca-pvm-api-qa--0000003`
  - Workbench: `ca-pvm-workbench-qa--0000004`
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
- `INV158888` is persisted with zero submission attempts and exactly one match
  to PO `1212021109`.

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

- Merge and deploy `feature/shoprite-item-uom-mappings`.
- Open `INV158888`, select its single matched Shoprite PO line, and choose the
  verified Shoprite UOM that represents Acumatica `BOX`.
- Confirm the candidate becomes `Ready`, has no blocking validation issues, and
  review the generated XML before the first manual Shoprite QA submission.
- CLI-authenticated smoke for protected API endpoints is blocked by Entra
  consent for Azure CLI against the API scope; browser sign-in remains the
  correct operator path.

The local host still does not have the .NET SDK installed; backend verification uses the SDK container with Docker socket access.
