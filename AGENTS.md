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
8. `docs/implementation-plans/qa-completion-and-production-rollout-plan.md`
9. `docs/runbooks/shoprite-qa-submission.md`
10. `docs/runbooks/exception-operations-qa.md`
11. `docs/runbooks/acumatica-push-notifications-qa.md`
12. `docs/runbooks/azure-qa-provisioning-playbook.md`

Use `.agents/skills/overseer/SKILL.md` for status, planning, readiness, review, branch hygiene, and next-work decisions. Use `.agents/skills/handoff/SKILL.md` when preparing a future-session handoff.

## Current Ground Truth

As of 2026-08-20:

- Azure is locked in. The QA estate is in CSP subscription
  `Azure subscription 1` (`1d0e7292-24e5-425e-870b-c56904b70da6`), resource
  group `rg-pvm-integrations-qa`.
- No production estate exists yet. Building it is Part Two of the rollout plan.
- Production automation Slices 1 through 8 are merged, deployed, and
  runtime-verified in QA. That covers submission safety, the payload archive,
  Service Bus and the worker, scheduled PO refresh, incremental reconciliation,
  push-notification ingestion, the automation policy with shadow mode, and
  exception operations.
- Live invoices `INV158888` and `INV158889` were matched, mapped, submitted to
  Shoprite QA, and accepted. Shoprite confirmed the payload on 2026-07-27.
- The emergency stop is proven on the automatic path and on the manual path.
  Both refusals are audited.
- QA automation policy is v7, mode `Disabled`, emergency stop clear. The
  automatic submit-command count is zero.
- Microsoft Entra workbench auth and app-managed users are implemented. The API
  refuses Azure CLI tokens, so Admin actions need a browser session.
- Production sending is not enabled and is blocked by the five gates in the
  rollout plan.

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

Read `docs/implementation-plans/qa-completion-and-production-rollout-plan.md`.
Slices 1 to 8 of the production automation plan are merged and deployed in QA.
The active work is Part One of that plan, which completes the QA evidence set,
and Part Two, which builds the production estate. Production sending stays
disabled until the five gates pass.

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
