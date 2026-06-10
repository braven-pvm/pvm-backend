# Current Project Status

Last updated: 2026-06-10

## Overall Status

Ready for the next Shoprite implementation slice.

Azure infrastructure and access are unblocked. The project should proceed with the Shoprite PO-pivoted invoice submission path.

## Active Priority

Implement Shoprite `VendorOrder` PO inbox ingestion.

Do this before switching invoice submission to the real Shoprite QA `VendorInvoice` client.

## Why This Is Next

The current design says the Shoprite PO is the pivot between Acumatica and Shoprite:

- Acumatica finalized invoice provides invoice truth.
- Shoprite PO provides delivery location, buyer/store/DC context, order-line context, and Shoprite item/GTIN context.
- Invoice candidates must match exactly one local PO before submission.

## Current Implementation State

Done:

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

- Shoprite `VendorOrder` PO inbox client/persistence/UI.
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

Most recent verification:

- Frontend lint passed.
- Frontend build passed.
- Backend direct `dotnet test` could not run because the local machine has no .NET SDK.
- Backend SDK-container test built and ran domain/application tests, but infrastructure Testcontainers tests could not reach Docker from inside the container.

Full backend verification requires .NET 10 SDK plus Docker/Testcontainers access.
