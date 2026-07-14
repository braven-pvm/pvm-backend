# Shoprite QA Invoice Submission Runbook

This runbook covers the QA-only Shoprite invoice upload slice. The Shoprite `VendorOrder` PO inbox is implemented for QA credential-backed refresh, and the workbench can seed a deterministic QA invoice candidate from a selected loaded PO. This lets us test Shoprite QA `VendorInvoice` acceptance before Acumatica staging invoice extraction is wired. Do not treat invoice submission as production-ready until Acumatica staging extraction, payload storage, and production hardening are complete. The QA workbench/API must remain protected by Microsoft Entra authentication and app-managed roles before real invoice/customer data is connected.

## Scope

The QA run proves:

- finalized Acumatica invoice data can be normalized into the canonical invoice model
- Shoprite validation runs before submission
- Shoprite invoice XML can be generated and inspected
- an operator can manually submit one invoice candidate
- submission attempts are persisted for audit and duplicate blocking

The QA run does not prove:

- production Shoprite credentials
- production Acumatica credentials
- automatic submission on invoice finalization
- Shoprite's live idempotency behavior
- dispatch timing enforcement

## Required Access

Acumatica staging, required for the later ERP-source test:

- base URL for the staging/sandbox tenant
- integration user credentials or OAuth client details
- tenant/company/branch context
- endpoint version and entity selection for source invoices
- access to finalized/released invoices
- access to invoice lines, customer account, customer location/ship-to, customer order/PO, totals, taxes, inventory IDs, UOM, and GTIN/barcode fields

Shoprite QA:

- Auto Download API base URL
- `VendorInvoice` endpoint availability
- username
- password
- confirmation that QA accepts XML payloads with `Content-Type: application/xml`
- expected success, validation-error, and duplicate/error response examples

For the PO inbox, the verified QA endpoint shape is:

```text
GET /api/VendorOrder?userName={userName}&password={password}
POST /api/VendorInvoice?userName={userName}&password={password}
```

The local workbench uses:

```text
POST /api/shoprite/purchase-orders/refresh
GET /api/shoprite/purchase-orders
GET /api/shoprite/purchase-orders/{id}
```

Operator access:

- workbench URL
- admin or submitter role
- permission to refresh, inspect, submit, and retry safe failures

## Environment Variables

Local defaults:

```powershell
ConnectionStrings__Pvm=Host=localhost;Port=54329;Database=pvm;Username=pvm;Password=pvm
NEXT_PUBLIC_API_BASE_URL=http://localhost:5000
```

Shoprite QA values, once the real client is enabled:

```powershell
Shoprite__BaseUrl=https://<shoprite-qa-host>/
Shoprite__Username=<qa-username>
Shoprite__Password=<qa-password>
Shoprite__InvoiceSubmissionMode=RealQa
```

Acumatica staging values, once the real connector is enabled:

```powershell
Acumatica__InvoiceSourceMode=RealQa
Acumatica__BaseUrl=https://devtest1.aboutitgroup.co.za/PVMGroup25R101D250625
Acumatica__Company=PVM
Acumatica__Branch=<branch-if-applicable>
Acumatica__Username=<integration-user>
Acumatica__Password=<integration-password>
Acumatica__EndpointName=Default
Acumatica__EndpointVersion=24.200.001
Acumatica__CountryCode=ZA
Acumatica__ParentCustomerAccounts__0=DEB2062
Acumatica__InvoiceDateFrom=2026-07-01T00:00:00+02:00
Acumatica__PageSize=100
```

Use `Acumatica__CustomerAccounts__N` only for explicit invoice-level customer
accounts. `DEB2062` is the Shoprite/Checkers parent account, not the customer ID
stored on store/DC invoices, so it must be configured under
`ParentCustomerAccounts`. The connector resolves all children through the
`Customer` endpoint and chunks invoice filters to keep request URIs bounded.
`InvoiceDateFrom` is mandatory in `RealQa` so the first refresh cannot import
the full historical closed-invoice population.

The instance path indicates Acumatica 2025 R1. Acumatica's official 2025 R1
integration examples use `Default/24.200.001`, but the selected endpoint and
`SalesInvoice` fields were confirmed from the live `swagger.json` on 2026-07-14.
The standard endpoint exposes `CustomerOrder`, `Details`, `TaxDetails`,
`DiscountTotal`, `Amount`, and `TaxTotal`. It does not expose an invoice-level
delivery location, which is acceptable because the matched Shoprite PO remains
the source of delivery GLN/location truth.

Do not expand `TaxDetails` on the paged `SalesInvoice` collection request. The
live instance rejects that optimized export because the taxes view has a BQL
delegate. The connector pages invoice summaries, then retrieves each selected
invoice by session entity ID with `Details,TaxDetails` expanded.

Do not commit real values. Use `.env` locally and managed secrets in hosted environments.

Required Acumatica integration-user access:

- sign in through `/entity/auth/login`
- read the `Default/24.200.001` endpoint schema
- read `SalesInvoice` headers, `Details`, and tax data
- read finalized `Open` and `Closed` invoices for the configured Shoprite accounts
- read customer order/PO reference, invoice/customer/location identifiers, currency,
  dates, totals, line inventory IDs, descriptions, quantities, UOM, prices, tax,
  and any exposed barcode/GTIN fields

Before live UAT, create or capture one finalized test invoice dated on or after
the configured cutover and verify its Shoprite PO number exists in the local PO
inbox. The invoice customer must be the store/DC child customer represented by
the PO delivery location, not parent account `DEB2062`. If the Default endpoint
does not expose a required GTIN, enrich it from the matched Shoprite PO line or
extend the endpoint contract minimally in `SM207060`; do not add a native
Acumatica connector.

## Local QA Startup

Start PostgreSQL:

```powershell
docker compose -f deploy/docker-compose.yml up -d
```

Run backend checks. On machines with the .NET 10 SDK installed:

```powershell
dotnet build backend/Pvm.sln
dotnet test backend/Pvm.sln
```

If only a .NET runtime is installed locally, use the SDK container:

```powershell
docker run --rm -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet build backend/Pvm.sln
docker run --rm -e TESTCONTAINERS_RYUK_DISABLED=true -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -v "${PWD}:/src" -v /var/run/docker.sock:/var/run/docker.sock -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet test backend/Pvm.sln
```

Start the API:

```powershell
dotnet run --project backend/src/Pvm.Api/Pvm.Api.csproj --urls http://localhost:5000
```

Start the workbench:

```powershell
cd frontend/workbench
npm ci
$env:NEXT_PUBLIC_API_BASE_URL="http://localhost:5000"
npm run dev
```

Open:

```text
http://localhost:3000/invoices
```

## Refresh Shoprite POs

Load Shoprite QA credentials into the API process as configuration:

```powershell
Shoprite__BaseUrl=https://b2b.shopriteholdingsqa.co.za/B2BWebAPISupplierServices/api
Shoprite__Username=<qa-username>
Shoprite__Password=<qa-password>
```

Do not print or commit the real values.

API:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/shoprite/purchase-orders/refresh
Invoke-RestMethod -Uri http://localhost:5000/api/shoprite/purchase-orders
```

Workbench:

- open `/purchase-orders`
- click `Refresh POs`
- confirm the QA order batch appears
- open a PO detail and confirm delivery location, line GTINs, and raw payload are visible

Expected current QA result:

- `VendorOrder` returns JSON with `orderField`
- current verified batch size was 40 orders
- delivery location is usually sourced from `buyerField`

Deployed QA operator smoke:

```text
https://ca-pvm-workbench-qa.blackbay-85d5b3d6.southafricanorth.azurecontainerapps.io/purchase-orders
```

Use the browser sign-in flow. CLI access tokens for the protected API may fail unless Azure CLI has consent for the API scope in Entra.

## Refresh Candidates

Fixture QA slice:

- `POST /api/invoices/refresh` imports the sanitized fixture at `backend/src/Pvm.Api/Features/Invoices/Fixtures/shoprite-invoice-basic.json`.
- The fixture creates invoice `INV342699282`.
- The fixture intentionally carries an unverified UOM warning, which is allowed in QA/staging.
- Candidate validation now also requires the invoice PO number to match exactly one local Shoprite PO.

API:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/invoices/refresh
Invoke-RestMethod -Uri http://localhost:5000/api/invoices/candidates
```

Workbench:

- open `/invoices`
- click `Refresh queue`
- confirm the invoice candidate appears

Expected result:

- status is `NeedsReview` until the candidate PO number exists in the local PO inbox
- validation shows blocking issue `missing-local-shoprite-po` when the PO is not loaded
- after PO match, validation may still show warning `unverified-shoprite-uom`
- generated XML is visible on the candidate detail page

## Seed A Candidate From A Shoprite PO

For the first Shoprite-side QA submission test, Acumatica staging is not required. Seed the candidate directly from a loaded Shoprite QA PO:

API:

```powershell
$po = (Invoke-RestMethod -Uri http://localhost:5000/api/shoprite/purchase-orders)[0]
Invoke-RestMethod -Method Post -Uri "http://localhost:5000/api/shoprite/purchase-orders/$($po.id)/seed-test-invoice"
```

Workbench:

- open `/purchase-orders`
- open a PO detail page
- click `Seed test invoice`
- review the generated invoice candidate

Expected result:

- invoice number is deterministic: `QA-INV-{purchaseOrderNumber}`
- Acumatica ID is deterministic: `QA-SEED-{purchaseOrderNumber}`
- candidate is linked to the selected local PO
- supplier GLN, delivery GLN, line GTIN, quantities, and amounts come from the PO
- validation may show `unverified-shoprite-uom` as a QA warning
- `Submit to Shoprite` is only enabled when the candidate has a matched local PO

## Validate Candidate Detail

API:

```powershell
$candidate = (Invoke-RestMethod -Uri http://localhost:5000/api/invoices/candidates)[0]
Invoke-RestMethod -Uri "http://localhost:5000/api/invoices/candidates/$($candidate.id)"
```

Workbench detail page should show:

- Acumatica invoice ID
- Shoprite PO number
- matched Shoprite PO context, if present
- supplier GLN
- store/DC GLN
- totals
- validation issues
- generated XML preview
- attempt history

Block submission if:

- any validation issue has severity `Blocking`
- the candidate already has a successful attempt
- the candidate has an ambiguous attempt requiring manual review

## Submit to Shoprite QA

Local stub mode:

- `POST /api/invoices/{id}/submit` calls the submission command path.
- When `Shoprite__InvoiceSubmissionMode` is absent or `LocalStub`, the registered local client returns a deterministic accepted response.
- This proves the local command, persistence, attempt history, and duplicate blocking path.

Real QA mode:

- When `Shoprite__InvoiceSubmissionMode=RealQa`, the API uses the real Shoprite QA `VendorInvoice` client.
- The client posts XML to `VendorInvoice?userName={userName}&password={password}`.
- Credentials must not be printed, logged, or copied into docs.

API:

```powershell
$candidate = (Invoke-RestMethod -Uri http://localhost:5000/api/invoices/candidates)[0]
Invoke-RestMethod -Method Post -Uri "http://localhost:5000/api/invoices/$($candidate.id)/submit"
```

Expected result:

- response status is `Submitted`
- candidate status becomes `Submitted`
- `canSubmit` becomes `false`
- attempt history contains one submitted attempt
- a second submit returns `409 Conflict` with status `DuplicateBlocked`

When the real Shoprite QA client is enabled:

- verify the outbound URL is the QA `VendorInvoice` endpoint
- confirm the endpoint shape is `VendorInvoice?userName={userName}&password={password}`
- confirm content type is `application/xml`
- capture the Shoprite response body and HTTP status
- compare the accepted/rejected response with the Shoprite API test tool

## Ambiguous Outcomes

Ambiguous outcomes include:

- timeout after sending the request
- network failure after the request may have reached Shoprite
- unclear Shoprite response where acceptance cannot be proven

Expected system behavior:

- submission result is `Ambiguous`
- attempt status is `Ambiguous`
- candidate cannot be retried automatically
- admin must review Shoprite and local state before retry

Manual review checklist:

1. Search Shoprite QA for the invoice number and PO number.
2. Confirm whether Shoprite accepted, rejected, or did not receive the invoice.
3. Compare the request payload hash and XML preview with the submitted payload.
4. If Shoprite accepted it, mark the local candidate as resolved/submitted once that admin action exists.
5. If Shoprite did not receive it, allow a controlled retry once that admin action exists.
6. If outcome cannot be proven, keep the candidate blocked and escalate to Shoprite support.

## Payloads and Audit

The current vertical slice stores request and response payload bodies directly on submission attempts so the workbench can show attempt history. The target production design is:

- PostgreSQL stores metadata, state, hashes, and payload locations
- blob storage stores raw request and response payloads
- credentials and sensitive headers are never stored
- audit events capture every automated and manual state transition

For MVP hardening, verify every attempt records:

- invoice candidate ID
- initiated by
- initiation mode
- status
- XML request payload or payload location
- request hash
- response status code
- response body or payload location
- response hash
- error message when present
- retry eligibility
- responsible role
- created timestamp

## Known Gaps Before Real QA

- Real Acumatica staging connector is not wired to refresh yet.
- Real Shoprite QA client is enabled only when `Shoprite__InvoiceSubmissionMode=RealQa`.
- Workbench authentication and roles are implemented for QA through Microsoft Entra sign-in and app-managed roles.
- Mapping admin pages for GLN, GTIN, UOM, pack, tax, and connection settings are not implemented yet.
- Blob payload archive is not implemented yet.
- Manual ambiguous-resolution actions are not implemented yet.
- Automatic finalization-triggered submission is excluded from MVP.

## Pass Criteria

For a QA demo, all of these should pass:

- backend build passes
- backend tests pass
- frontend lint passes
- frontend build passes
- `/health` returns `200`
- refresh creates the fixture candidate
- candidate detail shows validation and XML
- manual submit records a submitted attempt
- duplicate submit is blocked
- ambiguous failure behavior is proven by test or controlled stub
