# Shoprite REST Web Services V9.3 Discovery

Last researched: 2026-05-15

Primary source: `docs/specifications/Shoprite REST Web Services Guide V9.3.pdf`

## Scope Position

This document captures the initial Shoprite integration discovery for the Acumatica backoffice project.

Primary initial scope:

- Submit invoices from Acumatica to Shoprite.

Secondary potential scope:

- Fetch Shoprite purchase orders and create Acumatica sales orders from them.

Current architecture assumption:

- Build an external integration service between Acumatica and Shoprite.
- Do not build a custom Acumatica native connector for this phase.
- Use Acumatica REST/OData for ERP access.
- Use Shoprite REST Web Services for Shoprite B2B exchange.

## Key Finding

Shoprite's interface is REST as transport, but the business payloads are EDI-style GS1 documents. The system should therefore be designed as a document translation and workflow engine, not as a simple CRUD API bridge.

For invoice submission, our service will need to:

1. Read an eligible Acumatica invoice.
2. Resolve the originating Shoprite purchase order and store/DC GLN context.
3. Transform invoice header, totals, tax, and lines into Shoprite's GS1 invoice message layout.
4. Submit the document to Shoprite's `VendorInvoice` endpoint.
5. Store request, response, validation status, and retry state.
6. Prevent duplicate invoice submission after delivery has already taken place.

For purchase order ingestion, our service will need to:

1. Poll Shoprite's `VendorOrder` endpoint.
2. Parse order documents.
3. Validate item GTINs, GLNs, quantities, prices, dates, and order type.
4. Create matching Acumatica sales orders.
5. Acknowledge Shoprite orders only after successful local processing.

## Shoprite Environments

The guide defines separate QA and production endpoints.

QA:

```text
Base URL: https://externalservicesqa.shopriteholdings.co.za/b2bservice/
API base: https://externalservicesqa.shopriteholdings.co.za/b2bservice/api
External IP: 13.81.214.94
```

Production:

```text
Base URL: https://externalservices.shopriteholdings.co.za/b2bservice/
API base: https://externalservices.shopriteholdings.co.za/b2bservice/api
External IP: 104.40.189.79
```

The guide explicitly recommends whitelisting URLs where possible because Shoprite has moved servers to Azure and IPs may create future firewall issues.

## Security and Headers

Every request through Shoprite's Layer 7 gateway requires:

- `Authorization`: HTTP Basic auth using the supplier's auto-download user credentials.
- `ContractID`: same value shown for QA and production in the guide.
- `UIUser`: the current service account, supplier name, or consumer name for tracking and support.

The guide calls the Shoprite user an Auto Download user, B2B Service Account, or UIUser. Production users are created by the supplier's master user in the Supplier Portal. Test credentials are supplied by Shoprite Electronic Ordering Officers.

Headers to model in configuration:

```text
Authorization: Basic <base64(username:password)>
ContractID: <contract-id>
UIUser: <ui-user>
Accept: application/xml or application/json
Content-Type: application/xml or application/json
```

Implementation notes:

- Store credentials and contract IDs as environment secrets, not code.
- Treat QA and production as separate environment records.
- Log `UIUser`, endpoint, Shoprite document number, Acumatica document number, and correlation IDs, but never log Basic auth credentials.
- Use long enough HTTP timeouts; Shoprite examples use 300 seconds.

## Supported Shoprite Operations

The guide lists the following Layer 7 operations:

- `VendorClaim`
- `VendorOrder`
- `VendorInvoice`
- `VendorASN`
- `VendorRebate`

For current scope, the important operations are:

| Operation | Method | Purpose | Current Scope |
| --- | --- | --- | --- |
| `VendorInvoice` | `POST` | Upload new invoices | Primary |
| `VendorOrder` | `GET` | Fetch new available orders | Secondary |
| `VendorOrder?action=A` | `PUT` | Acknowledge processed orders | Secondary |
| `VendorOrder?action=Reset` | `PUT` | Reset orders for re-download | Support/admin only |

## Primary Flow: Acumatica Invoice to Shoprite

Shoprite's invoice flow is one-way submission:

```text
Acumatica invoice
  -> integration validation
  -> Shoprite GS1 invoice XML/JSON document
  -> POST /api/VendorInvoice
  -> success or error response
  -> store status and retry decision
```

Endpoint shape from the guide:

```text
POST https://externalservicesqa.shopriteholdings.co.za/b2bservice/api/VendorInvoice
POST https://externalservices.shopriteholdings.co.za/b2bservice/api/VendorInvoice
```

The guide's HTTP example says `content-type: application/json`, but the detailed invoice appendix is XML. We need to confirm with Shoprite whether production invoice submission should be XML, JSON, or either with matching `Accept` and `Content-Type` headers. Because the appendices are XML layouts and the guide says XML samples/XSDs are available from Shoprite, the safer implementation path is to model canonical invoice data first and generate the exact required XML or JSON serializer after we confirm the accepted payload type.

### Invoice Business Rules

Critical Shoprite rules:

- Each e-invoice must reference a valid Shoprite purchase order.
- Only one e-invoice is allowed per valid purchase order.
- Currency code must be valid.
- Credit memos and delivery notes are not accepted instead of an e-invoice.
- GTINs must be valid and match packed products.
- The e-invoice must match the paper invoice accompanying delivery.
- E-invoice must be submitted at least 2 hours before dispatch.
- Invoice reference number and grand totals must match paper exactly, including leading zeroes.
- Lines with zero quantity must not be sent.
- Do not send an e-invoice after delivery has already taken place if the system was down; the guide warns this causes duplicates.

These rules make duplicate prevention and workflow timing first-class requirements, not implementation details.

### Invoice Mapping Requirements

Header fields to map:

- Supplier GLN.
- Store/DC GLN.
- Invoice number, exactly matching the paper invoice.
- Invoice creation timestamp.
- Invoice effective/date field.
- Invoice type, normally `INVOICE`.
- Currency code, such as `ZAR`.
- Country of supply, such as `ZA`.
- Supplier VAT registration number.
- Shoprite purchase order number.
- Invoice totals exclusive, inclusive, and VAT amount.

Line fields to map:

- Line item number.
- Invoiced quantity.
- Unit or pack cost excluding tax.
- Unit or pack cost including tax.
- Transfer of ownership date.
- Product description.
- GTIN.
- Measurement unit: only `EA`, `CA`, `CS`, or `KG`.
- Pack size.
- Tax amount.
- Tax category: `STANDARD` or `ZERO`.
- Tax percentage.
- VAT tax type code.
- Line-level GUID/reference attributes where required by the layout.

Important cost rule:

- Shoprite multiplies line cost values by quantity. The invoice amount fields are cost per single unit, pack, or case supplied, not total line value.

Decimal rule:

- Cost values should use 4 decimals to keep totals aligned with the paper invoice.
- Variable weight pack size should ideally also use 4 decimals.

Tax rule:

- Tax percentage and currency/country codes must align to Shoprite's country table.
- For South Africa, the table allows `0.00` and `15.00`; zero-rated items need `ZERO` tax category and 0 tax percentage.

Free stock rule:

- Free stock must be sent as a separate invoice line with quantity and cost `0`, plus another line for the costed quantity.

Measurement rule:

- Shoprite accepts only `EA`, `CA`, `CS`, and `KG` on invoices.
- The measurement must align to the barcode, not just the pack size.

GTIN rule:

- Shoprite expects valid product barcodes/GTINs and warns that order GTINs may appear as 14 digits. For 13-digit barcodes, the leading zero may need to be dropped so the supplier ERP recognizes the product. We must validate this against our item master and Acumatica barcode storage.

## Secondary Flow: Shoprite Purchase Order to Acumatica Sales Order

Shoprite order flow is pull plus acknowledgement:

```text
GET /api/VendorOrder
  -> parse order batch
  -> create Acumatica sales order(s)
  -> persist local processing state
  -> PUT /api/VendorOrder?action=A with processed order numbers
```

The guide warns that if acknowledgement is not completed correctly, the same orders will keep being returned and no new orders/claims will be provided until acknowledgement issues are resolved.

Endpoint shapes:

```text
GET /api/VendorOrder
PUT /api/VendorOrder?action=A
PUT /api/VendorOrder?action=Reset
```

The acknowledgement payload is an array/list of order numbers. The guide shows XML `ArrayOfLong`, while the C# appendix serializes a JSON list. We need to test which format Shoprite expects in QA for `Content-Type`.

Polling guidance:

- Fetch orders.
- If orders are returned, acknowledge them after successful processing.
- Repeat fetch and acknowledgement until no more orders are available.
- Then sleep and retry after 15 minutes.
- Orders are not loaded between 23:30 and 06:30.
- Orders are fetched 40 at a time, though the guide says this number can change.

Purchase order fields to map into Acumatica:

- Shoprite PO number.
- Order date.
- Order type code: `220` normal, `258` allocation.
- Buyer/store/DC GLN.
- Store/DC branch code and name.
- Supplier GLN and supplier accounting/vendor IDs.
- Delivery store/DC GLN and branch code.
- Requested delivery date.
- Line number.
- Requested quantity.
- GTIN.
- Shoprite item ID and description.
- Supplier item ID if present.
- Pack size.
- Unit of measure, especially `EA` vs `KG`.
- Cost per pack excluding and including tax.
- Promotional deal/contract number.
- Special instructions.

Business rules for PO ingestion:

- Store and DC purchase orders use the same message layout, but DCs may use discount tags.
- Order discounts are informational; the order cost already includes negotiated discounts.
- Allocation orders use type code `258`; normal orders use `220`.
- Free stock appears as a separate order line with cost `0` and must later appear on the invoice.
- For catch weight items, order interpretation can differ materially from normal items.

## Data Model Implications

The integration service should have durable records for:

- Shoprite connection/environment configuration.
- Shoprite trading partner identity and GLNs.
- Store/DC GLN lookup table.
- Item/GTIN cross-reference.
- Purchase order inbox.
- Purchase order line details.
- Acumatica sales order linkage.
- Invoice outbox.
- Invoice line details.
- Submission attempts and responses.
- Acknowledgement attempts and responses.
- Duplicate-prevention keys.

Minimum duplicate-prevention keys:

- Shoprite PO number.
- Acumatica sales order number.
- Acumatica invoice number.
- Shoprite invoice `InstanceIdentifier` / invoice number.
- Supplier GLN + store/DC GLN + PO + invoice number.

## Document and Spec Slices

Keep this work split into focused documents instead of one large specification.

Recommended documentation structure:

- `docs/acumatica-2025-r2-integration-research.md`
  - Acumatica platform/API research.
- `docs/shoprite-rest-v9.3-discovery.md`
  - Shoprite REST/EDI discovery and source-derived rules.
- `docs/spec-slices/shoprite-invoice-submission.md`
  - Primary scope functional specification for Acumatica invoice to Shoprite.
- `docs/spec-slices/shoprite-po-to-sales-order.md`
  - Secondary scope functional specification for Shoprite PO to Acumatica sales order.
- `docs/spec-slices/shoprite-master-data.md`
  - GLN, store/DC, supplier, GTIN, pack size, UOM, and tax mapping.
- `docs/spec-slices/shoprite-operations-and-reconciliation.md`
  - Retry, acknowledgement, duplicate prevention, error handling, monitoring, and support operations.

Recommended sprint slices:

1. Discovery and fixture capture.
   - Obtain Shoprite XSDs/XML samples.
   - Obtain QA credentials.
   - Obtain Acumatica sample invoices/orders/items.
   - Build mapping inventory.
2. Invoice submission proof of concept.
   - Transform one Acumatica invoice into Shoprite invoice payload.
   - Submit to Shoprite QA.
   - Capture response/error behavior.
3. Invoice validation hardening.
   - Totals, tax, GTIN, GLN, UOM, duplicate prevention, and timing validation.
4. Shoprite PO ingestion proof of concept.
   - Poll QA orders.
   - Parse payload.
   - Create Acumatica sales order in sandbox.
   - Acknowledge only after local success.
5. Operationalization.
   - Retry queues.
   - Reconciliation jobs.
   - Dashboards/log search.
   - Manual reset/replay workflows.

## Open Questions

- Does Shoprite expect invoice submission as XML, JSON, or either in our supplier setup?
- Can Shoprite provide the latest XSDs and canonical sample XML for invoices and orders?
- What is the exact success response format for `VendorInvoice`?
- What are the exact validation error payloads for `VendorInvoice`?
- Are invoice submissions idempotent on invoice number, or can duplicate posts create duplicate records?
- Which Acumatica invoice type is the source: AR `Invoice`, SO `SalesInvoice`, or both?
- How will Acumatica store the originating Shoprite PO number?
- Where will Acumatica store Shoprite store/DC GLNs and branch codes?
- Are all products mapped by GTIN in Acumatica today?
- How do we represent pack/case/UOM conversion from Acumatica to Shoprite's accepted `EA`, `CA`, `CS`, and `KG` values?
- Are catch weight / variable weight products in initial scope?
- Are free stock lines in initial scope?
- Which countries/currencies are in initial scope?
- What is the operational cutoff for "2 hours before despatch" in our dispatch workflow?
- Should invoice submission be triggered by invoice release, shipment confirmation, delivery planning, or a separate approval step?

## Immediate Next Research Tasks

- Request Shoprite XSDs and official invoice/order sample payloads from B2B helpdesk or Electronic Ordering Officers.
- Get Shoprite QA Auto Download credentials and confirm QA ContractID/UIUser behavior.
- Use the Shoprite QA API test tool to inspect live method signatures and sample request/response types.
- Identify one real Acumatica invoice linked to a Shoprite purchase order and map every required Shoprite invoice field.
- Identify one real Shoprite purchase order sample and map it to Acumatica `SalesOrder`.
- Confirm whether Acumatica has complete GTIN, GLN, pack size, UOM, VAT, and country/currency data.
- Define invoice duplicate-prevention policy before any automated Shoprite submission.

