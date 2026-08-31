# Documentation Index

Use this index to orient quickly.

## Current Handoff

- `docs/handovers/2026-06-10-shoprite-project-handoff.md`
- `docs/status/current-project-status.md`

## Project Specs

- `docs/spec-slices/shoprite-invoice-upload-mvp.md`
- `docs/spec-slices/shoprite-po-pivot-invoice-submission.md`
- `docs/shoprite-rest-v9.3-discovery.md`
- `docs/acumatica-2025-r2-integration-research.md`
- `docs/architecture-stack-options.md`

## Implementation Plans

- `docs/implementation-plans/integration-admin-console-plan.md`
- `docs/implementation-plans/shoprite-invoice-upload-mvp-plan.md`
- `docs/implementation-plans/shoprite-production-automation-plan.md`
- `docs/implementation-plans/qa-completion-and-production-rollout-plan.md`
- `docs/implementation-plans/workbench-auth-and-roles-plan.md`

## Runbooks

- `docs/runbooks/shoprite-qa-submission.md`
- `docs/runbooks/exception-operations-qa.md`
- `docs/runbooks/shoprite-order-acknowledgement-qa.md`
- `docs/runbooks/production-deployment.md`
- `docs/runbooks/shoprite-po-refresh-scheduler-qa.md`
- `docs/runbooks/acumatica-invoice-reconciliation-qa.md`
- `docs/runbooks/acumatica-push-notifications-qa.md`
- `docs/runbooks/azure-qa-provisioning-playbook.md`
- `docs/runbooks/azure-provider-meeting-prep.md`
- `docs/runbooks/github-azure-oidc-qa.md`
- `docs/runbooks/workbench-auth-qa.md`

## External Specifications

- `docs/specifications/Shoprite REST Web Services Guide V9.3.pdf`

## Current Priority

Read `docs/implementation-plans/qa-completion-and-production-rollout-plan.md`
first. It carries the schedule, the gates, and the inputs that production waits
on.

## Background

Shoprite confirmed on 2026-07-27 that the real Acumatica-source QA invoice
submission is structurally sound and correct. Production automation Slices 1
through 8 are merged, deployed, and runtime-verified in QA. Automatic invoice
submission remains disabled and has never run. Read
`docs/status/current-project-status.md` before starting.
