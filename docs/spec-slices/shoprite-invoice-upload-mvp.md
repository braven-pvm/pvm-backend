# Shoprite Invoice Upload MVP

Last updated: 2026-05-18

## Purpose

Build the first working vertical slice for submitting finalized Acumatica invoices to Shoprite through Shoprite REST Web Services.

This MVP proves the hardest and riskiest integration path:

- Read finalized Acumatica invoice data.
- Validate Shoprite-specific invoice requirements.
- Generate Shoprite GS1 invoice XML.
- Let an operator manually submit one invoice at a time to Shoprite QA/staging.
- Persist full audit, payload, status, and retry history.

## Source Documents

- `docs/acumatica-2025-r2-integration-research.md`
- `docs/shoprite-rest-v9.3-discovery.md`
- `docs/architecture-stack-options.md`
- `docs/specifications/Shoprite REST Web Services Guide V9.3.pdf`

## MVP Scope

Included:

- Invoice candidate discovery from Acumatica.
- Manual operator selection and submission.
- Shoprite invoice XML generation.
- Shoprite QA/staging submission through `VendorInvoice`.
- Invoice validation and blocking rules.
- Duplicate prevention.
- Submission attempt history.
- Ambiguous failure handling.
- Focused Invoice Submission Workbench.
- Basic mapping/config management for Shoprite invoice submission.

Primary workflow:

```text
Finalized Acumatica invoices
  -> Invoice Submission Workbench candidates
  -> validation and mapping checks
  -> operator reviews generated Shoprite XML
  -> operator submits one invoice
  -> system posts to Shoprite QA VendorInvoice
  -> system records response/status/audit trail
```

## Explicitly Out of Scope

- Shoprite PO ingestion into Acumatica sales orders.
- Bulk invoice submission.
- Automatic event-triggered submission.
- Production Shoprite traffic.
- Catch weight / variable weight items.
- Multi-country / non-ZAR invoices.
- Raw XML editing.
- Admin overrides of invoice financial values.
- Full reporting warehouse.
- ASN, claims, rebate invoices.
- Native Acumatica custom connector.
- Full self-service BI/reporting frontend.

## Target End State

The final solution should support Acumatica event-driven submission:

```text
Acumatica invoice finalization event/webhook
  -> fetch invoice from Acumatica
  -> validate
  -> idempotency check
  -> auto-submit if clean and configured for Shoprite
  -> route exceptions to workbench
```

MVP must prepare for this by making manual and automatic submission use the same backend command:

```text
SubmitShopriteInvoice(invoiceId, initiatedBy, initiationMode)
```

Expected `initiationMode` values:

- `manual`
- `scheduled`
- `acumatica-finalized-event`

## Submission Trigger

MVP:

- Manual operator-approved submission.
- Operators select finalized invoices from the workbench.
- Submission is one invoice at a time with confirmation.

Final target:

- Event-triggered automatic submission for invoices that pass all validations and match configured Shoprite rules.
- Exceptions remain in the workbench.

## Invoice Candidates

MVP should show all relevant invoice candidates with validation status, not only perfectly valid invoices.

Candidate source:

- Acumatica finalized invoices.
- Initially pulled by manual refresh and/or scheduled refresh.
- No automatic submission in MVP.

Eligibility for submission:

- Invoice is finalized/released in Acumatica.
- Invoice customer/account belongs to configured Shoprite trading partner rules.
- Invoice ship-to/customer location maps to a known Shoprite DC.
- Invoice has a Shoprite PO number.
- Invoice has required line, tax, GTIN, UOM, pack, currency, and country data.
- Invoice is not already submitted under the duplicate-prevention key.

Invalid candidates remain visible with clear validation status and fix guidance.

## Shoprite Trading Partner and DC Mapping

The model must represent Shoprite as a trading partner with multiple delivery DCs.

Canonical structure:

```text
Trading Partner: Shoprite
  Supplier Account / Vendor Identity
    Acumatica Customer Account(s)
    Supplier GLN / Vendor IDs
    Delivery Locations / DCs
      Shoprite DC GLN
      Shoprite branch/DC code
      Acumatica ship-to / customer location mapping
      invoice rules/config
```

MVP mapping rule:

- Shoprite DC identity comes from Acumatica ship-to/customer location mapped to Shoprite DC GLN in the admin console.
- Missing or unknown DC mapping blocks submission and creates a mapping task.
- Admin can resolve the mapping from the invoice detail/workbench and revalidate.

## Data Correction Model

Corrections are split:

- Business document data is fixed in Acumatica.
- Integration mappings/config are fixed in the admin console.

Examples fixed in Acumatica:

- Invoice number.
- PO number.
- Quantities.
- Prices.
- VAT/tax amounts.
- Invoice line data.

Examples fixed in integration/admin console:

- GLN mappings.
- GTIN exception mappings.
- UOM/pack mappings.
- Tax category mappings.
- Shoprite account/environment config.

Operators cannot directly edit generated XML.

## Payload Format

MVP uses XML-first generation.

Architecture:

```text
Acumatica invoice DTO
  -> canonical invoice model
  -> Shoprite invoice XML
```

Reasons:

- The Shoprite guide's detailed invoice layout is XML.
- Shoprite references XSDs.
- XML gives the clearest conformance and support target.

Open validation:

- Confirm with Shoprite QA whether invoice submission accepts XML, JSON, or both.
- Obtain official XSDs and canonical invoice samples.

## Idempotency and Duplicate Prevention

Duplicate-prevention key:

```text
supplier GLN + store/DC GLN + Shoprite PO number + Acumatica invoice number
```

Rules:

- Duplicate submission under the same key is blocked.
- All submission attempts are persisted.
- Shoprite timeouts or network failures after send are marked ambiguous.
- Ambiguous submissions require manual review before retry.
- Automatic retry is not allowed after ambiguous outcome until Shoprite idempotency behavior is proven.

## Validation Rules

The workbench should show validation status and block submission until required validations pass.

Required validations:

- Invoice finalized/released.
- Shoprite trading partner match.
- Known Shoprite DC mapping.
- Supplier GLN present.
- Store/DC GLN present.
- Shoprite PO number present.
- Invoice number present.
- No duplicate idempotency key.
- South Africa / ZAR only.
- Required GTINs resolved.
- UOM/pack mapping resolved to Shoprite `EA`, `CA`, `CS`, or `KG`.
- Production requires verified UOM/pack mappings.
- QA/staging may allow unverified UOM/pack mappings with explicit warning.
- Tax data maps to Shoprite tax category and percentage rules.
- Grand totals match exactly in production.
- Zero-quantity lines are not submitted.
- Catch weight / variable weight items are blocked.

## GTIN Mapping

Line mapping precedence:

1. Acumatica invoice line/item barcode or GTIN.
2. Integration mapping override for `Acumatica item + UOM/pack/context`.
3. If unresolved, block submission and create mapping task.

The admin console owns validation and exception mappings, but Acumatica remains the preferred source for item/barcode data.

## UOM and Pack Mapping

MVP rule:

- Use Acumatica UOM/pack as the starting point.
- Integration maps to Shoprite `EA`, `CA`, `CS`, or `KG`.
- Unknown mappings block submission.
- Admin can reconfigure mappings.
- Verified mappings are reusable.
- Unverified mappings are allowed in QA/staging only with warning.
- Production requires verified mappings.

## Tax and Totals

MVP supports South Africa / ZAR only.

Tax mapping:

- Use Acumatica tax data.
- Validate strictly against Shoprite's accepted tax rules.
- For taxable South African lines:
  - `dutyFeeTaxCategoryCode = STANDARD`
  - `dutyFeeTaxPercentage = 15.00`
- For zero-rated/non-taxable lines:
  - `dutyFeeTaxCategoryCode = ZERO`
  - `dutyFeeTaxPercentage = 0.00`

Totals:

- Generate cost values to 4 decimals where required by Shoprite.
- Recalculate generated XML totals.
- Compare generated totals to Acumatica invoice totals.
- Production uses zero tolerance for grand total mismatch.
- Show line-level reconciliation when totals differ.

Admin can configure tax category mappings, but cannot manually override invoice tax amounts.

## Free Stock

MVP supports free stock only when Acumatica represents it explicitly as separate zero-cost invoice lines.

Rules:

- Explicit zero-cost invoice lines can be mapped.
- If free stock is implied but not represented as lines in Acumatica, block/flag unsupported for MVP.
- Operators cannot manually add free stock lines in the workbench.

## Catch Weight

Catch weight / variable weight items are excluded from MVP.

If detected:

- Block submission.
- Show reason.
- Route to unsupported-scenario task.

## Dispatch Timing

Business context:

- Acumatica does not contain the real physical dispatch event.
- When stock is picked in the warehouse, Acumatica automatically generates/finalizes the invoice and dispatch slip/document.
- Actual physical dispatch is planned outside Acumatica.
- Dispatch timing therefore cannot be reliably enforced from Acumatica data in MVP.

MVP rule:

- Do not enforce Shoprite's 2-hour dispatch cutoff in software.
- Require finalized invoice status.
- Keep this as a known operational dependency.

Future option:

- If the external dispatch schedule becomes available, integrate it and add timing validation.

## Admin Console: Invoice Submission Workbench

MVP admin surface is a focused Invoice Submission Workbench, not a general-purpose integration portal.

Minimum screens:

- Invoice candidates list with validation status.
- Invoice detail with Acumatica source fields.
- Canonical invoice model view.
- Generated Shoprite XML preview.
- Validation/errors panel.
- Submit action with confirmation.
- Submission attempts/history.
- Ambiguous/failure queue.
- Basic mapping/config pages for GLN, GTIN, UOM, pack, tax, and Shoprite connection settings.

Manual actions:

- Refresh invoice candidates from Acumatica.
- Revalidate invoice.
- Submit validated invoice.
- Retry safe failures.
- Mark ambiguous submission reviewed.
- Resolve mapping tasks.

No raw XML editing.

## Roles and Permissions

MVP roles:

- Viewer.
- Submitter.
- Admin.

Viewer:

- Read-only access to candidates, validation results, payload previews, attempts, and audit history.

Submitter:

- Viewer rights.
- Submit validated invoices.
- Retry safe failures.

Admin:

- Full access.
- Edit mappings.
- Edit connection config.
- Execute manual state actions.
- Retry/replay where permitted.
- Manage operators/users.

## Error Handling

Shoprite validation errors should be classified into actionable workbench tasks.

Classification buckets:

- Fix in Acumatica.
- Fix in integration config.
- External/support required.

Every failed submission should capture:

- Error text.
- Classification.
- Recommended fix location.
- Retry eligibility.
- Last request payload.
- Last response payload.
- Responsible role.

Ambiguous outcomes:

- Timeout or network failure after send.
- Mark as `Submission Ambiguous`.
- Manual review required before retry.

## Audit and Retention

MVP retention:

- Retain all invoice submission records indefinitely.
- Retain raw request and response payloads indefinitely.
- Redact credentials and sensitive headers.
- Preserve immutable audit history.

Required audit data:

- Idempotency key.
- Acumatica invoice reference.
- Shoprite PO number.
- Supplier GLN.
- DC/store GLN.
- Operator.
- Action.
- Timestamp.
- Validation snapshot.
- Generated payload hash and stored payload location.
- Shoprite response status/body.
- State transition history.

## Environment and Deployment

MVP deployment target:

- Azure Container Apps.
- Managed PostgreSQL.
- Azure Service Bus.
- Blob Storage.
- Key Vault or equivalent managed secrets.

Environment stance:

- Use full Acumatica staging/sandbox.
- Use Shoprite QA/staging.
- Preserve sanitized fixtures for automated tests.
- Do not build confidence from mocked data alone.

## First Vertical Slice

Build the first slice backend-led but with minimal UI.

Slice goals:

1. Pull one finalized Acumatica invoice.
2. Normalize to canonical invoice.
3. Validate it.
4. Generate Shoprite XML.
5. Show it in a minimal workbench page.
6. Submit to Shoprite QA manually.
7. Record full attempt history.

## MVP Success Criteria

MVP success is a QA-only technical milestone.

Acceptance criteria:

- Operator can refresh invoice candidates from Acumatica.
- System shows validation status and blocks invalid invoices.
- Operator can preview generated Shoprite XML.
- Operator can submit one valid invoice to Shoprite QA.
- System records exact request payload, response payload, status timeline, operator, timestamp, and idempotency key.
- Duplicate submission is blocked.
- Ambiguous timeout/network failure enters manual-review state.
- Validation failures explain whether to fix Acumatica data or integration mapping/config.

MVP is not production ready until separate production hardening is completed.

