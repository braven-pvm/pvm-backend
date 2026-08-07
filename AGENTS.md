# Agent Onboarding

This repo is the PVM backend, integration, admin, and reporting platform for
Acumatica Cloud ERP and trading-partner workflows. The Shoprite invoice-upload
QA milestone is complete; the active project is production automation.

## Start Here

Before implementing or reviewing work, read these in order:

1. `README.md`
2. `docs/README.md`
3. `docs/handovers/2026-06-10-shoprite-project-handoff.md`
4. `docs/spec-slices/shoprite-invoice-upload-mvp.md`
5. `docs/spec-slices/shoprite-po-pivot-invoice-submission.md`
6. `docs/implementation-plans/shoprite-production-automation-plan.md`
7. `docs/implementation-plans/integration-admin-console-plan.md`
8. `docs/runbooks/shoprite-qa-submission.md`
9. `docs/runbooks/acumatica-push-notifications-qa.md`
10. `docs/runbooks/azure-qa-provisioning-playbook.md`

Use `.agents/skills/overseer/SKILL.md` for status, planning, readiness, review, branch hygiene, and next-work decisions. Use `.agents/skills/handoff/SKILL.md` when preparing a future-session handoff.

## Current Ground Truth

As of 2026-08-07:

- Azure is locked in and the QA estate is provisioned or provisionable.
- The active QA estate is in CSP subscription `Azure subscription 1`
  (`1d0e7292-24e5-425e-870b-c56904b70da6`).
- Cost visibility and budget access are confirmed.
- Required Azure resource providers are registered.
- The QA workbench/API infrastructure exists in the CSP subscription.
- Microsoft Entra workbench auth and app-managed users are implemented.
- Real Shoprite QA `VendorOrder` refresh and `VendorInvoice` submission are implemented.
- Real Acumatica QA finalized-invoice refresh is deployed.
- Live invoice `INV158888` was matched to Shoprite PO `1212021109`, mapped,
  submitted to Shoprite QA, and confirmed by Shoprite as structurally sound and
  correct.
- Production automation foundations are partially implemented but automatic
  submission is not enabled.
- The production automation plan is approved. A functional Admin console is a
  required release gate, with Admin retaining full controlled access.
- Production automation Slices 1 through 3 are merged, deployed, and
  runtime-verified in QA.
- Slice 4 scheduled Shoprite PO refresh is merged, deployed, and runtime-verified,
  including its controlled freshness-alert fault test.
- Slice 5 incremental Acumatica reconciliation is merged, deployed, and
  runtime-verified in QA, including bootstrap, daily lookback, two incremental
  windows, cursor advancement, stale-alert recovery, and no automatic send.
- Slice 6 Acumatica push-notification ingestion is implemented on
  `feature/acumatica-push-notification-ingestion` and awaits review and QA
  deployment. The Acumatica QA destination is not active.
- Explicit migrations, the concurrency-safe submission-operation state machine,
  immutable payload archiving, hash verification, and transition audit are
  operational.
- QA candidate `QA-INV-1212503708` completed the deployed archive path on
  2026-08-03 with four verified payload blobs and a successful Shoprite QA
  response.

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

Review Slice 6, Acumatica push-notification ingestion, merge it only after
approval, and verify it in QA with
`docs/runbooks/acumatica-push-notifications-qa.md`. Keep the Acumatica push
destination inactive until the synthetic endpoint checks pass. Automatic
invoice submission remains disabled.

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
az role assignment list --scope /subscriptions/1d0e7292-24e5-425e-870b-c56904b70da6 --include-inherited --output table
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
