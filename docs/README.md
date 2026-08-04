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
- `docs/implementation-plans/workbench-auth-and-roles-plan.md`

## Runbooks

- `docs/runbooks/shoprite-qa-submission.md`
- `docs/runbooks/shoprite-po-refresh-scheduler-qa.md`
- `docs/runbooks/azure-qa-provisioning-playbook.md`
- `docs/runbooks/azure-provider-meeting-prep.md`
- `docs/runbooks/github-azure-oidc-qa.md`
- `docs/runbooks/workbench-auth-qa.md`

## External Specifications

- `docs/specifications/Shoprite REST Web Services Guide V9.3.pdf`

## Current Priority

Shoprite has confirmed that the real Acumatica-source QA invoice submission is
structurally sound and correct. Production automation Slices 1 through 3 are
merged, deployed, and runtime-verified in QA. Slice 4, scheduled Shoprite PO
refresh and persisted run visibility, is implemented and awaiting review and QA
deployment. Automatic invoice submission remains disabled. Read
`docs/status/current-project-status.md` and both production implementation plans
before starting.
