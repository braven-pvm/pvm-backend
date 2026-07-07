# Shoprite Project Handoff

Date: 2026-06-10  
Repo: `F:\Repositories\backoffice.acumatica`  
Branch observed: `main`

## Executive State

Status: **PO inbox implementation in progress on `feature/shoprite-po-inbox`**.

Infrastructure is not blocking the Shoprite project. Azure is locked in, the QA estate exists, and subscription/resource access is sufficient for provisioning and deployment. Shoprite QA `VendorOrder` access has been verified with the supplier QA credentials.

## What Is Implemented

Backend:

- Shoprite `VendorOrder` client, parser, PO inbox persistence, refresh/list/detail endpoints.
- Invoice candidate matching to one local PO by captured PO number.
- .NET solution under `backend/Pvm.sln`.
- Domain canonical invoice model.
- Shoprite invoice validation rules.
- Shoprite invoice XML generator.
- Acumatica fixture DTO normalizer.
- Submission command path with duplicate/ambiguous handling.
- EF Core persistence for invoice candidates, attempts, audit/user auth state.
- Real infrastructure `ShopriteInvoiceClient` for `VendorInvoice`.
- API endpoints for invoice refresh, candidate list/detail, revalidate, submit, attempts.
- Current API registration uses `LocalShopriteInvoiceClient` for the invoice submission slice.

Frontend:

- PO inbox list/detail pages under `/purchase-orders`.
- Next.js workbench under `frontend/workbench`.
- Microsoft Entra/NextAuth sign-in.
- Invoice list/detail pages.
- Submit action path.
- Admin user management page.

Infrastructure:

- Azure Bicep under `infra/azure`.
- QA GitHub Actions workflow under `.github/workflows/deploy-qa.yml`.
- Azure Container Apps deployment exists for API and workbench.
- Managed PostgreSQL, Service Bus, Blob Storage, Key Vault, ACR, App Insights, and Log Analytics exist in the QA estate.

Docs:

- Shoprite MVP spec: `docs/spec-slices/shoprite-invoice-upload-mvp.md`
- PO-pivot design: `docs/spec-slices/shoprite-po-pivot-invoice-submission.md`
- QA runbook: `docs/runbooks/shoprite-qa-submission.md`
- Azure playbook: `docs/runbooks/azure-qa-provisioning-playbook.md`

## Current Decisions

- Shoprite PO is the integration pivot.
- Every Shoprite invoice submission must link to exactly one Shoprite PO.
- The local PO inbox should be the source for invoice submission context; do not call `VendorOrder` once per invoice in the submit path.
- Delivery location must support both DCs and direct-to-store destinations.
- In current QA `VendorOrder` samples, delivery identity is on `buyer.gln`; `shipTo` can be null.
- Manual MVP submission and future automatic submission must use the same backend command.
- MVP excludes PO-to-Acumatica SO creation, bulk submission, automatic finalization-triggered submission, catch weight, production Shoprite traffic, and raw XML editing.

## Azure Status

Confirmed on 2026-06-10:

- Tenant: `cf6de706-07fd-492e-9ff7-13234a0961a6`
- Subscription: `PVM-01` / `51497af4-8223-42c4-a2ef-f6f625094d2f`
- Current CLI user: `developer@pvm.co.za`
- `developer@pvm.co.za` has `Owner` at subscription scope.
- `pvm-backend-qa-deploy` has `Contributor` at subscription scope and `Owner` on `rg-pvm-integrations-qa`.
- Required providers are registered.
- No subscription policy assignments were returned.
- Budget and usage access work from CLI.
- Subscription-scope Bicep `what-if` succeeds.

Caveat: Entra tenant-level operations can still require tenant admin permissions even when Azure resource provisioning is unblocked.

Sensitive note: the Microsoft partner PDF in downloads includes a temporary Beyers admin password. Do not copy it into repo docs or terminal output. Confirm it has been changed.

## Verification Snapshot

Run on 2026-06-10:

- `npm --prefix frontend/workbench run lint`: passed.
- `npm --prefix frontend/workbench run build`: passed.
- Direct `dotnet test backend\Pvm.sln`: blocked because no local .NET SDK was installed.
- SDK-container `dotnet test backend/Pvm.sln`: domain and application tests passed; two infrastructure tests failed because Testcontainers could not reach Docker from inside the SDK container.

Interpretation:

- Frontend is healthy.
- Backend code compiled in SDK container.
- Infrastructure test failure is an environment/Testcontainers-Docker access issue, not a proven product defect.
- Full backend verification still needs a host with .NET 10 SDK and Docker/Testcontainers access.

## Known Gaps

Before real Shoprite QA confidence:

- Deploy and smoke-test the new PO inbox against the QA Container Apps/PostgreSQL estate.
- Real Acumatica staging invoice refresh is not implemented.
- API still registers `LocalShopriteInvoiceClient` for submission.
- Blob payload archive is not implemented.
- Mapping/admin pages for GLN, GTIN, UOM, pack, tax, and connection settings are not implemented.
- Manual ambiguous-resolution actions are not implemented.
- Real `VendorInvoice` duplicate/idempotency behavior is not proven.
- Official Shoprite XSDs and canonical samples are still desired.

## Next Recommended Slice

Verify and deploy **Shoprite PO inbox ingestion** before wiring real invoice submission.

Why:

- The PO is the pivot for Shoprite trading context.
- Invoice candidates must match the local PO inbox by PO number.
- The matched PO supplies delivery location, GTIN/order-line context, and buyer/store/DC data.
- Submitting real invoices without durable PO context would bake in the wrong boundary.

Suggested branch:

```text
feature/shoprite-po-inbox
```

Implemented local scope on `feature/shoprite-po-inbox`:

1. Add `IShopritePurchaseOrderClient`.
2. Implement `VendorOrder` HTTP client using QA credentials.
3. Add persistence entities/tables for PO headers, PO lines, raw payload hash/body location, location context, and refresh metadata.
4. Add refresh command/handler.
5. Add API endpoints:
   - `POST /api/shoprite/purchase-orders/refresh`
   - `GET /api/shoprite/purchase-orders`
   - `GET /api/shoprite/purchase-orders/{id}`
6. Add minimal PO inbox workbench list/detail.
7. Add invoice candidate matching to local PO by PO number.
8. Update validation to block candidates with no matching PO or multiple matching POs.
9. Add tests for PO parsing, VendorOrder request shape, and persistence constraints.

Remaining after this slice:

- Deploy the PO inbox branch to QA.
- Set Shoprite QA secrets in Key Vault/Container Apps config using `Shoprite__BaseUrl`, `Shoprite__Username`, and `Shoprite__Password`.
- Wire Acumatica staging invoice refresh.
- Confirm the exact `VendorInvoice` authentication shape before enabling the real invoice submission client.

Local smoke on 2026-07-07:

- `POST /api/shoprite/purchase-orders/refresh` imported 40 Shoprite QA POs into local PostgreSQL.
- `/purchase-orders` rendered the imported PO data.
- Fixture invoice refresh stayed blocked with `missing-local-shoprite-po`, which is expected because the fixture PO does not exist in the current QA VendorOrder batch.

Do not include:

- Creating Acumatica sales orders from POs.
- Acknowledging Shoprite POs.
- Production credentials.
- Automatic invoice finalization submission.

## Useful Files For The Next Agent

Implementation:

- `backend/src/Pvm.Api/Program.cs`
- `backend/src/Pvm.Api/Features/Invoices/InvoiceEndpoints.cs`
- `backend/src/Pvm.Api/Features/Submissions/LocalShopriteInvoiceClient.cs`
- `backend/src/Pvm.Infrastructure/Shoprite/ShopriteInvoiceClient.cs`
- `backend/src/Pvm.Infrastructure/Persistence/PvmDbContext.cs`
- `backend/src/Pvm.Infrastructure/Persistence/Repositories/EfInvoiceCandidateRepository.cs`
- `frontend/workbench/app/invoices/page.tsx`
- `frontend/workbench/app/invoices/[id]/page.tsx`

Specs/runbooks:

- `docs/spec-slices/shoprite-po-pivot-invoice-submission.md`
- `docs/spec-slices/shoprite-invoice-upload-mvp.md`
- `docs/runbooks/shoprite-qa-submission.md`
- `docs/runbooks/azure-qa-provisioning-playbook.md`

## Commands

Frontend:

```powershell
npm --prefix frontend/workbench run lint
npm --prefix frontend/workbench run build
```

Backend:

```powershell
dotnet test backend/Pvm.sln
```

Azure:

```powershell
az account show
az resource list -g rg-pvm-integrations-qa --output table
az deployment sub what-if --location southafricanorth --template-file infra/azure/main.bicep --parameters infra/azure/main.parameters.qa.json postgresAdminPassword='<dummy-for-what-if-only>'
```

Git:

```powershell
git status --short
git diff --check
```

## Working Tree At Handoff

Known pending docs at the time this housekeeping was started:

- `docs/runbooks/azure-qa-provisioning-playbook.md` modified with partner access confirmation.
- `docs/spec-slices/shoprite-invoice-upload-mvp.md` modified to include PO-pivot details.
- `docs/runbooks/azure-provider-meeting-prep.md` untracked.
- `docs/spec-slices/shoprite-po-pivot-invoice-submission.md` untracked.

Do not discard these. They are relevant project documentation.

## Suggested Skills

- Use `overseer` for status/planning/review.
- Use `handoff` before ending a long session or changing agents.
- Use `tdd` if implementing the PO inbox slice.
- Use `diagnose` for any auth, Azure deployment, or Shoprite request failure.
