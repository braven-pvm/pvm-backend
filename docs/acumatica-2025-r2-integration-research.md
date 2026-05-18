# Acumatica Cloud ERP 2025 R2 Integration Research

Last researched: 2026-05-15

## Executive Summary

Acumatica Cloud ERP 2025 R2 exposes a mature integration surface, but the main public ERP APIs are not GraphQL-first. The confirmed integration paths are:

- Contract-based REST API over configurable web service endpoints.
- Contract-based SOAP API over the same endpoint contract model.
- Legacy screen-based SOAP API.
- OData for read-heavy reporting and BI, especially generic inquiries.
- Import/export scenarios for batch data movement and operational imports.
- Business events, push notifications, and webhooks for event-driven integration.
- Customization projects and custom endpoints for exposing business-specific fields, entities, and actions.

GraphQL appears in 2025 R2 release material in relation to the Shopify commerce connector transition, not as a general Acumatica ERP public API for our own integrations. We should treat Acumatica REST, OData, and event notifications as the default integration backbone until verified inside our tenant.

## Version Context

Acumatica announced 2025 R2 general availability on September 23, 2025. The release emphasizes the modern UI, AI Studio, dashboard/reporting improvements, business event/import scenario improvements, and industry features across distribution, commerce, construction, professional services, and manufacturing.

Important 2025 R2 platform items for our project:

- Modern UI and personalization can affect screen layouts, but contract-based APIs are designed to remain stable against UI labels and customizations.
- Dashboard/reporting improvements and Generic Inquiry Query Language (GIQL) matter for reporting design.
- Business events gained operational improvements, including email preview/editing and synchronous event processing according to release material.
- Import scenarios gained better field search tooling.
- Commerce connector enhancements include Amazon settlement report integration and Shopify connector GraphQL usage.

Sources:

- Acumatica 2025 R2 release page: https://www.acumatica.com/cloud-erp-software/2025-r2/
- Acumatica 2025 R2 press release: https://www.acumatica.com/corporate-newsroom/press-releases/acumatica-2025-r2/
- Contract REST SalesOrder entity docs: https://help.acumatica.com/Wiki/ShowWiki.aspx?PageID=22f2d9a6-c0d8-4909-a430-415d21d50eb1&wikiname=HelpRoot_Dev_Integration
- I320 Advanced Data Retrieval 2022 R1, sales-order batch and detail examples: https://openuni.acumatica.com/wp-content/uploads/2017/08/I320_WebServicesDataRetrievalAdvanced_2022R1.pdf
- I310 Basic Data Retrieval 2022 R1, item availability GI and inventory quantity retrieval examples: https://openuni.acumatica.com/wp-content/uploads/2017/08/I310_WebServicesDataRetrievalBasic_2022R1.pdf
- I300 Basic Data Retrieval 2019 R2, OData, REST, and LastModified examples: https://openuni.acumatica.com/wp-content/uploads/2017/08/I300_WebServicesDataRetrievalBasic_2019R2.pdf
- Integration Development Guide, REST filters, inquiry retrieval, push notifications, and webhooks: https://www.acumatica.com/media/2020/09/AcumaticaERP_IntegrationDevelopmentGuide.pdf
- Community note on AR Invoice vs SalesInvoice endpoint distinction: https://community.acumatica.com/develop-integrations-with-web-services-apis-289/how-to-get-balaned-invoice-via-rest-api-34032

## Integration Path Map

| Path | Best Use | Write Support | Reporting Support | Notes |
| --- | --- | --- | --- | --- |
| Contract REST API | Primary app-to-ERP sync, CRUD, actions, attachments | Yes | Limited to endpoint contract shape | Main API candidate for sync service. Endpoint OpenAPI 2.0 schemas are available per endpoint. |
| Contract SOAP API | Legacy enterprise integrations or vendor tooling that prefers WSDL | Yes | Limited | Same contract model as REST. Use only if a partner/tool requires SOAP. |
| Screen-based SOAP API | Legacy form-driven automation | Yes | Limited | Older and tightly coupled to forms. Avoid for new work unless forced. |
| OData via Generic Inquiries | BI, reporting extracts, dashboards, read-only sync | No, effectively read-oriented | Strong | Good for reporting and bulk reads. Must expose GIs and control access rights. |
| DAC-based OData | Lower-level read access to DAC data | No, read-oriented | Strong but more technical | Needs more validation in our tenant and access model. |
| Import/export scenarios | Initial loads, periodic bulk imports/exports, operational data movement | Yes via import scenarios | Export scenarios | Good for batch and business-user-visible mappings. Less ideal for low-latency sync. |
| Business events + push/webhooks | Change detection, notifications, downstream triggers | Trigger only | Event payloads only | Use to avoid pure polling where possible. |
| Custom endpoints/extensions | Business-specific API surface | Yes | Depends | Needed for custom fields, UDFs, custom screens, or missing entities/actions. |
| Native connectors | Shopify, BigCommerce, Amazon, tax, payments, shipping | Connector-specific | Connector-specific | Useful where built-in connectors match exact needs. Not a replacement for our integration layer. |
| GraphQL | Not confirmed as general Acumatica ERP API | Unknown | Unknown | Confirmed in release copy only for Shopify connector transition. Do not design around it yet. |

## Contract-Based REST API

This should be the primary integration path for a sync service that creates, updates, releases, or otherwise acts on Acumatica records.

Confirmed capabilities:

- External systems can get, process, create, and update Acumatica ERP records through web services.
- The contract-based REST API works through web service endpoints configured on `Web Service Endpoints (SM207060)`.
- Endpoints define top-level entities, detail entities, linked entities, fields, and actions.
- System endpoints named `Default` are preconfigured. Custom endpoints can be created or existing endpoints extended when the default contract is insufficient.
- Endpoint contracts are intended to remain stable even when UI labels are customized.
- Custom fields and user-defined fields can be accessed if the integration knows the data view and field names.
- REST schemas are available as OpenAPI 2.0 / Swagger JSON:
  - Endpoint schema: `https://<tenant-host>/entity/<EndpointName>/<EndpointVersion>/swagger.json`
  - Instance schema: `https://<tenant-host>/entity/swagger.json`
- The instance-level REST API includes sign-in, sign-out, and version methods.

Authentication:

- Acumatica supports OAuth 2.0 authorization for applications.
- It also documents direct REST sign-in at `/entity/auth/login`, but for a production integration service we should prefer OAuth/client application registration where possible.

Open questions for our tenant:

- Which Default endpoint versions are available in our Acumatica 2025 R2 tenant? Community sources indicate 2025 R2 has `Default/25.200.001`, but we should verify directly from `SM207060`.
- Which entities/actions needed by our sync are missing from the Default endpoint?
- Which custom fields/UDFs are business-critical and need explicit endpoint exposure?
- What license/API concurrency/request-rate limits apply to our subscription?

Primary source:

- Integration Development Guide: https://www.acumatica.com/media/2020/09/AcumaticaERP_IntegrationDevelopmentGuide.pdf

## OData for Reporting

OData is the clearest path for reporting-oriented reads, especially where we can model data through Generic Inquiries.

Confirmed capabilities:

- Acumatica supports exposing Generic Inquiry results through OData.
- The generic inquiry OData base URL is:
  - `https://<tenant-host>/t/<TenantName>/api/odata/gi`
- Appending `/$metadata` returns metadata for exposed inquiries.
- Appending `/<GI_Name>` retrieves data for a specific exposed inquiry.
- OData supports filtering and ordering, for example with `$filter` and `$orderby`.
- Access is constrained by the user account's rights.
- Acumatica supports OData 4.0 with documented exceptions.

Recommended use:

- Build reporting extracts around stable Generic Inquiries.
- Use GI naming/versioning conventions for our integration reports.
- Treat OData output as read models, not operational write APIs.
- For complex operational reporting, create purpose-built Generic Inquiries rather than stitching too much inside the external service.

Sources:

- Generic Inquiry OData access docs surfaced by Acumatica help search:
  - https://help.acumatica.com/%28W%2815%29%29/Wiki/ShowWiki.aspx?pageid=dac328c5-5dae-43af-b18a-d7a52374633d
  - https://help.acumatica.com/%28W%288%29%29/Wiki/ShowWiki.aspx?PageID=5d97a93d-45e0-466e-ba5e-77e1ccf96643&wikiname=HelpRoot_ReportingTools
- Generic Inquiry data retrieval:
  - https://help.acumatica.com/Wiki/ShowWiki.aspx?PageID=7990b4d9-1f40-4654-9494-7b2f6abfd023&wikiname=HelpRoot_ReportingTools

## Import and Export Scenarios

Import/export scenarios are a configured integration mechanism for moving data between external sources and Acumatica forms. They are useful for initial data loads, recurring batch integrations, and cases where finance/operations users need visibility into mappings and import history.

Confirmed 2025 R2 training coverage includes:

- Importing new master records.
- Updating imported records.
- Deleting incorrectly imported records.
- Importing and updating auto-numbered records.
- Importing master-detail records.
- Updating or deleting detail lines.
- Applying actions to imported or exported records.
- Importing records with attributes.
- Exporting records.

Use this path for:

- Initial migration/import work.
- Bulk corrections.
- Scheduled files from trading partners when real-time API sync is unnecessary.
- Processes that must mirror UI form actions.

Avoid this as the only integration mechanism for:

- Low-latency sync.
- High-frequency transactional writes.
- Integrations needing explicit idempotency and API-level observability.

Sources:

- I100 Integration Scenarios 2025 R2: https://openuni.acumatica.com/courses/reporting/i100-integration-scenarios/
- Import Scenarios form docs: https://help.acumatica.com/%28W%288%29%29/Wiki/ShowWiki.aspx?PageID=254e8347-6bac-469d-8f14-dbe383740475&wikiname=HelpRoot_FormReference

## Business Events, Push Notifications, and Webhooks

For sync, eventing matters because polling every table/report is inefficient and can miss intent. Acumatica has business events and push notification/webhook concepts that can notify or trigger downstream processing when configured conditions occur.

Confirmed concepts:

- Business events monitor actions, data changes, or conditions in the system.
- Subscribers can include email, push, SMS, import scenario execution, task creation, and related actions.
- Push notification definitions include a destination and a data query defining changes for which notifications are sent.
- The Integration Development Guide includes push notifications and webhooks sections.

Recommended use:

- Use business events/push/webhooks to trigger our integration service for changed business objects.
- Keep REST API reads as the source of truth after an event arrives; do not assume event payloads contain the complete canonical object.
- Use polling/OData fallback for reconciliation and missed-event recovery.

Sources:

- Business Events docs: https://help.acumatica.com/Wiki/ShowWiki.aspx?pageid=85d4ed6e-f498-4683-86f9-bdb5a2164c6d
- Push Notifications docs: https://help.acumatica.com/%28W%281%29%29/Wiki/ShowWiki.aspx?pageid=db168ac0-acde-4c01-a664-bb98088297be
- Integration Development Guide: https://www.acumatica.com/media/2020/09/AcumaticaERP_IntegrationDevelopmentGuide.pdf

## GraphQL Assessment

No primary source found so far confirms a general-purpose Acumatica ERP GraphQL API for external integrations in 2025 R2.

What is confirmed:

- Acumatica 2025 R2 release material says the Shopify connector moved to GraphQL for faster commerce synchronization.
- This appears to mean Acumatica's connector talks to Shopify's GraphQL API, not that Acumatica exposes GraphQL to external clients.

Working assumption:

- Do not build our architecture around Acumatica GraphQL.
- If GraphQL is desired internally, build our own GraphQL/BFF layer on top of Acumatica REST/OData after we understand the domain and access patterns.

Source:

- 2025 R2 release page, Shopify connector section: https://www.acumatica.com/cloud-erp-software/2025-r2/

## Recommended Architecture Direction

For our integration, sync, and reporting system:

1. Use Acumatica contract REST as the authoritative write/action API.
2. Use OData Generic Inquiries as the first reporting extraction mechanism.
3. Use business events/push/webhooks for change triggers where available.
4. Use scheduled reconciliation jobs to guard against missed events and API failures.
5. Use import/export scenarios for migration, backfills, and operator-controlled bulk operations.
6. Generate typed clients from endpoint `swagger.json` once we have tenant access.
7. Create custom endpoints only for missing entities, missing actions, or business-specific custom fields.
8. Keep an internal canonical model separate from Acumatica endpoint DTOs to avoid coupling our system to endpoint-version churn.

## Current Primary Use Cases

Custom native connectors are not part of the current plan. The first investigation path should assume an external integration/reporting service that uses Acumatica REST, OData, and event mechanisms.

Initial use cases:

1. Retrieve invoices for specific customers/accounts.
   - Likely path: contract REST API for account-scoped operational lookups; OData Generic Inquiries for reporting-style invoice lists and history.
   - Key validation: invoice entity fields, customer/account identifiers, document status filters, date filters, pagination, and access rights.
2. Create sales orders.
   - Likely path: contract REST API through the Default endpoint or a minimal custom endpoint if required fields/actions are missing.
   - Key validation: required order fields, customer lookup, inventory item lookup, warehouse/location rules, tax/shipping behavior, order release/hold behavior, and idempotency strategy.
3. Read inventory levels.
   - Likely path: OData Generic Inquiry for reporting/bulk availability; REST endpoint lookup if a real-time operational availability check is required.
   - Key validation: item, warehouse, location, lot/serial, allocation, available-for-shipment, available-for-sale, and timestamp semantics.
4. Reporting.
   - Likely path: OData Generic Inquiries as the first reporting source, with REST used for drill-through or actions.
   - Future direction: incremental refresh through changed-date filters, business events, push notifications, or webhooks, backed by scheduled reconciliation jobs.
   - Key validation: finance-owned report definitions, row-count parity with Acumatica UI, incremental fields, historical corrections, and event coverage.

## Use Case Investigation Notes

### 1. Retrieve Invoices for Specific Customers/Accounts

The first design decision is which Acumatica invoice object we mean:

- `Invoice` maps to Accounts Receivable invoices and memos on `AR301000`.
- `SalesInvoice` maps to Sales Orders invoices on `SO303000`.

This distinction matters because sales-order-generated invoices may need to be queried through `SalesInvoice`, not only through the generic AR `Invoice` endpoint. Community evidence from a 2026 Acumatica thread shows a user failing to retrieve a balanced invoice through `/entity/Default/24.200.001/Invoice`, with the accepted answer pointing them to `SalesInvoice` for invoices created from the SO module.

Recommended investigation path:

- Confirm whether our customer-facing invoices are AR invoices, SO invoices, or both.
- Inspect the 2025 R2 endpoint schema for both entities:
  - `/entity/Default/<version>/Invoice`
  - `/entity/Default/<version>/SalesInvoice`
- Test filters by customer/account, reference number, document type, status, invoice date, due date, and last-modified timestamp.
- Decide whether operational invoice lookup should use REST and whether invoice reporting/history should use OData Generic Inquiries.
- Validate PDF/report download separately if invoice document rendering is required.

Working API shape to validate:

```text
GET /entity/Default/<version>/Invoice?$filter=CustomerID eq '<customer>'&$select=ReferenceNbr,Type,Status,Date,DueDate,Balance,Amount
GET /entity/Default/<version>/SalesInvoice?$filter=CustomerID eq '<customer>'&$select=ReferenceNbr,Type,Status,Date,CustomerID
```

Open risk:

- Field names can differ by endpoint version and entity. We should not hard-code until we retrieve the tenant's `swagger.json`.

### 2. Create Sales Orders

Acumatica documents `SalesOrder` on the Default endpoint as the contract REST entity for sales-order integration. The training material uses `Default/20.200.001/SalesOrder`, and 2025 R2 examples use the same entity pattern with newer endpoint versions.

Recommended path:

- Use contract REST `PUT /entity/Default/<version>/SalesOrder`.
- Start with a minimal order on hold, then add lines, then test release/fulfillment behavior.
- Use the Default endpoint first. Only create a custom endpoint if required fields are missing.

Fields/actions to validate:

- Header: `OrderType`, `CustomerID`, `CustomerOrder`, `Date`, `RequestedOn`, branch, location, currency, hold flag.
- Lines: `InventoryID`, `OrderQty`, `UOM`, `UnitPrice`, warehouse, location, subitem, tax category, discounts.
- Workflow: hold/remove hold, shipment creation, payment attachment, taxes, freight, fulfillment.
- Idempotency: store our external order ID in a stable Acumatica field or custom field and query before create.

Working API shape to validate:

```text
PUT /entity/Default/<version>/SalesOrder
{
  "OrderType": { "value": "SO" },
  "CustomerID": { "value": "<customer>" },
  "CustomerOrder": { "value": "<external-order-id>" },
  "Details": [
    {
      "InventoryID": { "value": "<sku>" },
      "OrderQty": { "value": 1 },
      "WarehouseID": { "value": "<warehouse>" }
    }
  ]
}
```

Open risk:

- Implementations often need fields not exposed by the Default endpoint, such as branch, subitem, warehouse location, tax fields, or custom attributes. If those are required in our Acumatica configuration, the smallest acceptable customization is an endpoint extension, not a native connector.

### 3. Read Inventory Levels

There are two separate inventory questions:

- Operational availability for one item/warehouse/location.
- Reporting/bulk inventory levels across many items and warehouses.

Acumatica training material explicitly warns that retrieving item quantities one item at a time through the contract REST API can produce many database requests and slow performance. For bulk inventory availability, it recommends exporting from a Generic Inquiry such as Item Availability Data. The source example uses a GI based on `PX.Objects.IN.INSiteStatus` with results including `InventoryID`, `Warehouse`, `QtyAvailable`, and `QtyOnHand`.

Recommended path:

- For reporting/bulk availability: create or use a Generic Inquiry and expose it through OData.
- For point lookup: validate `InventorySummaryInquiry` or a REST endpoint/inquiry call with item and warehouse parameters.
- Avoid polling every item through `StockItem` unless the result set is small.

Fields to validate:

- `InventoryID`
- Warehouse
- Location
- Subitem
- Lot/serial number if used
- Quantity on hand
- Quantity available
- Quantity available for shipment
- Quantity allocated
- Timestamp or last activity marker

Working API shapes to validate:

```text
PUT /entity/Default/<version>/InventorySummaryInquiry?$expand=Results
{
  "InventoryID": { "value": "<sku>" },
  "WarehouseID": { "value": "<warehouse>" }
}
```

```text
GET /t/<TenantName>/api/odata/gi/<InventoryAvailabilityGI>?$filter=InventoryID eq '<sku>'
```

Open risk:

- "Inventory level" must be defined by business semantics. `QtyOnHand`, `QtyAvailable`, `QtyAvailableForShipment`, and allocated quantities are not interchangeable.

### 4. Reporting and Incremental/Event-Based Refresh

Reporting should start with OData Generic Inquiries because Acumatica's OData path is built around exposing Generic Inquiry results. REST is still useful for drill-through, actions, and entity-specific lookups.

Incremental options:

- REST `$filter` against `LastModified` or `LastModifiedDateTime` where the entity exposes it.
- OData filters against GI columns that expose source-table modified timestamps.
- Push notifications configured from a Generic Inquiry or built-in definition.
- Business events for business-level triggers.
- Scheduled reconciliation jobs for completeness.

Acumatica's integration guide documents REST filtering with examples such as:

```text
$filter=ItemStatus eq 'Active' and LastModified gt datetimeoffset'<timestamp>'
```

It also documents push notifications with webhook, message queue, and SignalR destinations. Webhooks send HTTP POST notifications. Notification payloads include inserted rows, deleted rows, query name, company, a transaction ID, and timestamp. The transaction ID can be used for duplicate detection, and inserted/deleted row comparison can be used to identify updates.

Recommended reporting path:

- Phase 1: full or filtered OData extracts from finance-approved Generic Inquiries.
- Phase 2: incremental extracts using last-modified columns and high-water marks.
- Phase 3: push notifications/webhooks to trigger near-real-time refresh.
- Always: scheduled reconciliation to handle missed events, corrections, and deleted/voided records.

Reporting validation checklist:

- Report owner and canonical UI screen/GI.
- Required filters and dimensions.
- Expected row counts for known periods.
- Required timestamp columns for incremental extraction.
- How voids, reversals, corrections, and backdated postings appear.
- Whether push notification queries avoid aggregation, grouping, heavy joins, and GI formulas.

## Initial Technical Risks

- API license limits: Acumatica licenses may restrict API users, concurrent API requests, and requests per minute.
- Endpoint versioning: 2025 R2 likely has newer endpoint versions, but older endpoints can remain available for compatibility. We need to choose intentionally.
- Customization drift: Custom endpoints require maintenance across Acumatica upgrades.
- Event reliability: Business events/webhooks should trigger sync, but reconciliation jobs are still required.
- Reporting consistency: OData/GI reports must be versioned and tested against finance-owned definitions.
- Tenant/company scoping: URLs and schema can vary by tenant/company; the integration must model this explicitly.
- Auth lifecycle: OAuth token refresh, secret rotation, and revocation must be first-class.

## Next Research Tasks

- Get access to our Acumatica sandbox/tenant.
- Export the available endpoint list from `Web Service Endpoints (SM207060)`.
- Download and commit the `swagger.json` for the selected Default endpoint.
- Enumerate core objects needed for phase 1:
  - Customers/accounts/contacts
  - Inventory/items
  - Inventory levels by item, warehouse, and location
  - Sales orders/invoices/payments
  - Purchase orders/bills
  - Warehouses/stock availability
  - Projects/jobs if relevant
  - GL/AP/AR reporting extracts
- Identify custom fields/UDFs used in our business processes.
- Test OAuth client registration and token refresh flow.
- Configure a minimal event/webhook proof of concept for one object type.
- Build one OData Generic Inquiry reporting extract and verify row counts against the Acumatica UI.
