# Shoprite PO-Pivot Invoice Submission Design

Last updated: 2026-05-20

## Purpose

Formalize the invoice submission flow where the Shoprite purchase order is the integration pivot between Acumatica and Shoprite.

This design supersedes the earlier assumption that the invoice flow can rely mainly on Acumatica customer/location mapping. Acumatica remains the accounting source of truth for finalized invoices, but the Shoprite purchase order is the source of truth for Shoprite trading context.

## Core Decision

Every Shoprite invoice submission must be linked to a Shoprite purchase order.

The PO is the pivot for:

- Shoprite order number.
- Buyer/store/DC GLN.
- Buyer-assigned location code and description.
- Order type, including normal and allocation orders.
- Shoprite item/GTIN context.
- Expected quantities, prices, and order line structure.
- Supplier/vendor identity as Shoprite sees it.

Acumatica is the source of truth for:

- Finalized invoice status.
- Acumatica invoice number.
- Invoice date and effective accounting dates.
- Actual invoiced quantities.
- Final invoice prices, totals, VAT, and currency.
- Customer/account and captured PO reference.
- Sales order and shipment/invoice linkage.

## Confirmed QA Observations

Using the Shoprite QA `VendorOrder` endpoint with the supplier QA credentials returned a live batch:

- Endpoint: `GET /api/VendorOrder?userName={userName}&password={password}`
- HTTP status: `200 OK`
- Format: JSON
- Batch size returned: `40` orders
- Order types present:
  - `220: Normal`
  - `258: Allocation`
- Lines per order in the sampled batch: `1` to `6`

In the sampled QA orders, delivery identity was carried primarily on `buyer`, not `shipTo`:

```text
buyer.gln = Shoprite store/location GLN
buyer.additionalPartyIdentification:
  BUYER_ASSIGNED_IDENTIFIER_FOR_A_PARTY = store/location code
  BUYER_ASSIGNED_DESCRIPTION_FOR_A_PARTY = store/location name
```

The sampled orders had `shipTo`, `shipFrom`, `inventoryLocation`, and `ultimateConsignee` as `null`. The integration must therefore support location extraction by priority, not by a single hard-coded field.

## Target Flow

```text
Shoprite VendorOrder refresh
  -> store PO inbox/cache
  -> Acumatica invoice candidate refresh
  -> match finalized invoice to PO number
  -> build canonical invoice from Acumatica invoice + Shoprite PO
  -> validate
  -> generate GS1 invoice XML
  -> operator manually submits in MVP
  -> POST VendorInvoice
  -> persist attempt, response, and audit trail
```

## Step 1: Refresh Shoprite PO Inbox

The system polls Shoprite `VendorOrder` and stores returned POs in a local durable inbox.

Stored data:

- Raw Shoprite response payload.
- Payload hash.
- Shoprite PO number.
- Order type code and label.
- Supplier GLN.
- Buyer/store/DC GLN.
- Buyer-assigned location code.
- Buyer-assigned location name.
- Order lines.
- GTINs.
- Quantities.
- Prices.
- VAT/tax context when present.
- Source endpoint and environment.
- First seen timestamp.
- Last seen timestamp.
- Processing/acknowledgement state.

The local inbox is the lookup source during invoice submission. The submit path should not call `VendorOrder` once per invoice unless an operator explicitly performs a refresh/recovery action.

## Step 2: Refresh Acumatica Invoice Candidates

The system pulls finalized Acumatica invoices for configured Shoprite customer/accounts.

Candidate filters:

- Invoice is finalized/released.
- Invoice belongs to a configured Shoprite customer/account.
- Invoice is not already submitted or blocked by an ambiguous submission.
- Invoice has a captured Shoprite PO number.
- Invoice is in the supported company/country/currency scope.

MVP trigger:

- Manual operator refresh.
- Optional scheduled refresh if low risk.

Future trigger:

- Acumatica invoice finalization event or polling delta.

## Step 3: Match Invoice to PO

The invoice candidate must match exactly one Shoprite PO record by the PO number captured on the Acumatica invoice.

Matching outcomes:

| Outcome | Behavior |
| --- | --- |
| One matching PO | Continue validation. |
| No matching PO | Block submission and create a PO refresh/support task. |
| Multiple matching POs | Block submission and require admin review. |
| PO exists but unsupported type | Block submission or route by configured order type policy. |

The matched PO must be persisted on the invoice candidate as a stable link so later retries use the same context.

## Step 4: Resolve Shoprite Delivery Location

The integration resolves a canonical `ShopriteDeliveryLocation` from the matched PO.

Priority order:

1. `shipTo.gln`, if populated.
2. `buyer.gln`, if `shipTo` is not populated.
3. `inventoryLocation.gln`, if neither `shipTo` nor `buyer` gives the destination and Shoprite confirms this use.
4. Manual mapping override, only with admin approval and audit trail.

For the current QA sample, `buyer.gln` is the active source.

The resolved delivery location stores:

- Location type: `store`, `dc`, or `unknown`.
- Shoprite GLN.
- Shoprite location code.
- Shoprite location name.
- Source field used, such as `buyer` or `shipTo`.
- Environment.
- Last confirmed from PO timestamp.

The integration should not assume all deliveries are to DCs. Direct-to-store deliveries use the same delivery location model.

## Step 5: Build Canonical Invoice

The canonical invoice combines Acumatica invoice truth with Shoprite PO context.

Header mapping:

| Canonical field | Source |
| --- | --- |
| Supplier GLN | Configuration and/or Shoprite PO seller GLN |
| Supplier VAT number | Configuration |
| Receiver/buyer GLN | Resolved Shoprite delivery location, pending Shoprite confirmation |
| Ship-to GLN | Resolved Shoprite delivery location |
| Shoprite PO number | Acumatica invoice PO reference matched to Shoprite PO |
| Invoice number | Acumatica finalized invoice |
| Invoice date | Acumatica finalized invoice |
| Currency | Acumatica invoice, MVP restricted to `ZAR` |
| Country | Configuration, MVP restricted to `ZA` |
| Totals and VAT | Acumatica finalized invoice |

Line mapping:

| Canonical field | Preferred source |
| --- | --- |
| Line number | Acumatica invoice, reconciled against PO line where possible |
| Quantity | Acumatica invoice |
| Unit/pack price | Acumatica invoice, validated against Shoprite rules |
| VAT/tax amount | Acumatica invoice |
| GTIN | Shoprite PO line and Acumatica item/barcode mapping |
| Description | Shoprite PO line or Acumatica line, with Shoprite PO preferred when exact invoice text is not mandated |
| UOM/pack | Mapping derived from Acumatica and validated against Shoprite allowed values |
| Shoprite item identifiers | Shoprite PO line |

## Step 6: Validate

Blocking validations:

- Invoice has finalized/released status.
- Invoice customer/account belongs to configured Shoprite trading partner scope.
- Invoice has a PO number.
- PO number exists in the local Shoprite PO inbox.
- Matched PO has a resolvable delivery GLN.
- Supplier GLN is present.
- Supplier VAT number is present.
- Invoice number is present.
- No successful previous submission exists for the duplicate-prevention key.
- No ambiguous previous submission is unresolved.
- All invoice lines have valid GTIN mapping.
- UOM/pack mapping resolves to accepted Shoprite invoice values.
- Currency is `ZAR`.
- Country is `ZA`.
- Tax category and percentage are supported.
- Zero-quantity lines are excluded or blocked according to policy.
- Catch weight/variable weight scenarios are blocked for MVP.

Warning validations:

- Acumatica UOM mapping is unverified in QA.
- PO price differs from invoice price but invoice totals remain valid.
- Shoprite location type cannot be classified as store or DC but GLN is available.

## Step 7: Submit Invoice

MVP submission is operator-approved and one invoice at a time.

Submission target:

```text
POST /api/VendorInvoice?userName={userName}&password={password}
Content-Type: application/xml
Accept: application/xml or application/json
```

The system generates GS1 `invoiceMessage` XML from the canonical invoice.

Credentials must not appear in logs, audit events, browser-visible error messages, or stored request URLs.

## Step 8: Persist Attempt and State

Every attempt records:

- Invoice candidate ID.
- Matched Shoprite PO ID.
- Idempotency key.
- Initiated by.
- Initiation mode.
- Request payload hash.
- Request payload storage location.
- Response status code.
- Response payload hash.
- Response payload storage location.
- Parsed Shoprite status, when available.
- Retry eligibility.
- Error classification.
- Created timestamp.

Do not mutate historical attempts.

## Idempotency Key

Use:

```text
supplier GLN + delivery location GLN + Shoprite PO number + Acumatica invoice number
```

Future hardening may add:

- Shoprite response identifier.
- Generated invoice instance identifier.
- Acumatica internal invoice ID.
- Payload hash.

## Local PO Inbox and Acknowledgement Policy

The PO inbox must retain POs even after Shoprite acknowledgement.

Initial MVP stance:

- Store POs locally from `VendorOrder`.
- Do not auto-create Acumatica sales orders in MVP.
- Do not make invoice submission depend on re-pulling the PO from Shoprite.
- Treat acknowledgement as a separate controlled workflow, because incorrect acknowledgement can affect future order availability.

Future sales-order automation:

```text
VendorOrder PO
  -> validate
  -> create Acumatica Sales Order
  -> persist Acumatica SO linkage
  -> acknowledge Shoprite PO only after successful local processing
```

## Admin Workbench Requirements

PO inbox:

- Refresh Shoprite POs.
- View PO list.
- Search by PO number, GLN, location code, location name, GTIN, order type.
- View raw/sanitized PO payload.
- Show linked Acumatica invoice and sales order when available.

Invoice candidates:

- Refresh Acumatica invoices.
- Match candidate to PO.
- Show PO-derived context next to invoice-derived context.
- Show validation issues.
- Preview generated XML.
- Submit validated invoice.
- Show attempt history.

Mappings/config:

- Shoprite customer/account scope.
- Supplier GLN and VAT number.
- Delivery location mapping and classification.
- GTIN mapping.
- UOM/pack mapping.
- Tax mapping.

## Data Model Additions

Minimum conceptual tables/entities:

- `shoprite_purchase_orders`
- `shoprite_purchase_order_lines`
- `shoprite_delivery_locations`
- `shoprite_invoice_candidates`
- `shoprite_invoice_candidate_po_links`
- `shoprite_invoice_submission_attempts`
- `shoprite_item_mappings`
- `shoprite_uom_pack_mappings`
- `shoprite_tax_mappings`

## Open Questions

- Should invoice `ReceiverEAN`, `buyer.gln`, and `shipTo.gln` all use the resolved delivery location GLN for both store and DC deliveries?
- Does Shoprite expect a separate corporate receiver GLN while `shipTo.gln` carries the store/DC GLN?
- Are the current QA direct-store orders representative of production payloads?
- Which Acumatica invoice field reliably stores the Shoprite PO number?
- Does Acumatica already store the Shoprite PO line number or only the PO header number?
- Should allocation orders (`258`) be included in MVP invoice submission or initially blocked?
- What exact acknowledgement policy should be used for `VendorOrder` once PO ingestion is active?
- What is Shoprite's duplicate rejection behavior for repeated `VendorInvoice` submissions?

## MVP Acceptance Criteria

- Operator can refresh Shoprite QA POs into the local inbox.
- System stores at least the 40-order batch returned by QA without losing raw payload.
- Operator can refresh finalized Acumatica invoice candidates for configured Shoprite accounts.
- Candidate with a PO number resolves to exactly one local Shoprite PO.
- Candidate detail shows Acumatica invoice data and Shoprite PO/location data side by side.
- Candidate validation blocks missing PO, missing GLN, duplicate, unresolved GTIN, unresolved UOM/pack, and unsupported tax/currency.
- Generated invoice XML uses Acumatica invoice financial truth and Shoprite PO delivery/location context.
- Operator can submit one valid candidate to Shoprite QA `VendorInvoice`.
- Attempt history and duplicate prevention are persisted.
