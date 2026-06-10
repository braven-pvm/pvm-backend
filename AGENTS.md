# Agent Onboarding

This repo is the PVM backend, integration, admin, and reporting platform for Acumatica Cloud ERP and trading-partner workflows. The active project is the Shoprite invoice-upload MVP.

## Start Here

Before implementing or reviewing work, read these in order:

1. `README.md`
2. `docs/README.md`
3. `docs/handovers/2026-06-10-shoprite-project-handoff.md`
4. `docs/spec-slices/shoprite-invoice-upload-mvp.md`
5. `docs/spec-slices/shoprite-po-pivot-invoice-submission.md`
6. `docs/runbooks/shoprite-qa-submission.md`
7. `docs/runbooks/azure-qa-provisioning-playbook.md`

Use `.agents/skills/overseer/SKILL.md` for status, planning, readiness, review, branch hygiene, and next-work decisions. Use `.agents/skills/handoff/SKILL.md` when preparing a future-session handoff.

## Current Ground Truth

As of 2026-06-10:

- Azure is locked in and the QA estate is provisioned or provisionable.
- `developer@pvm.co.za` has subscription `Owner` on `PVM-01`.
- Cost visibility and budget access are confirmed.
- Required Azure resource providers are registered.
- The QA workbench/API infrastructure exists.
- Microsoft Entra workbench auth and app-managed users are implemented.
- The local invoice submission vertical slice exists but still uses fixture invoice data and a local Shoprite stub client.
- The next implementation slice is Shoprite `VendorOrder` PO inbox ingestion.

## Important Design Decisions

- Shoprite invoice submission is PO-pivoted. The Shoprite PO is the source of truth for delivery location, buyer/store/DC context, and Shoprite item context.
- Acumatica remains the source of truth for finalized invoice status, invoice number, quantities, prices, VAT, totals, and captured PO reference.
- MVP submission is manual and one invoice at a time.
- Future automatic submission must call the same backend command path as manual submission.
- No raw XML editing in the UI.
- No production Shoprite traffic during MVP.
- Catch weight and variable weight scenarios are excluded from MVP.
- Physical dispatch timing is not available in Acumatica and is not enforced in MVP.

## Active Next Slice

Implement Shoprite PO inbox ingestion:

1. Add a `VendorOrder` client using Shoprite QA credentials.
2. Persist raw PO payloads and extracted PO/location/line context.
3. Add `POST /api/shoprite/purchase-orders/refresh`.
4. Add PO list/detail workbench views.
5. Match invoice candidates to local POs by captured PO number.
6. Use PO-derived delivery location and GTIN context in validation/generation.

Do not jump straight to real `VendorInvoice` submission until the PO inbox is stored and invoice candidates can match to exactly one local PO.

## Verification

Frontend:

```powershell
npm --prefix frontend/workbench run lint
npm --prefix frontend/workbench run build
```

Backend:

```powershell
dotnet test backend/Pvm.sln
```

If the local machine lacks the .NET 10 SDK, install the SDK or use a dev environment where Testcontainers can reach Docker. The SDK-container command can build, but infrastructure tests that use Testcontainers need Docker socket access.

General:

```powershell
git diff --check
git status --short
```

## Azure Notes

Use `docs/runbooks/azure-qa-provisioning-playbook.md` as the source of truth for Azure. The provider PDF in downloads contained a temporary password; treat that PDF as sensitive and do not copy credentials into the repo.

Useful checks:

```powershell
az account show
az role assignment list --scope /subscriptions/51497af4-8223-42c4-a2ef-f6f625094d2f --include-inherited --output table
az resource list -g rg-pvm-integrations-qa --output table
az consumption budget list --output table
```

## Linear

Linear was seeded from the implementation plan, but the MCP connector had workspace/auth issues. If Linear updates are needed, use `LINEAR_API_KEY` from the local `.env` without printing or committing it.

## Branch Hygiene

Current branch may contain documentation changes. Before implementing a new slice:

- Review `git status --short`.
- Keep stakeholder/provider docs separate from implementation changes where practical.
- Use a focused branch such as `feature/shoprite-po-inbox`.
- Do not revert user or prior-agent changes unless explicitly asked.
