# Shoprite QA Invoice Submission Runbook

This runbook covers the QA-only Shoprite invoice upload slice. The current local vertical slice uses a sanitized fixture and a local Shoprite stub client. Do not treat it as production-ready until Acumatica staging extraction, Shoprite QA credentials, and payload storage are wired to real services. The QA workbench/API must remain protected by Microsoft Entra authentication and app-managed roles before real invoice/customer data is connected.

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

Acumatica staging:

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
- `ContractID`
- `UIUser`
- confirmation that QA accepts XML payloads with `Content-Type: application/xml`
- expected success, validation-error, and duplicate/error response examples

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
Shoprite__ContractId=<qa-contract-id>
Shoprite__UiUser=<qa-ui-user>
```

Acumatica staging values, once the real connector is enabled:

```powershell
Acumatica__BaseUrl=https://<acumatica-staging-host>/
Acumatica__Tenant=<tenant>
Acumatica__Branch=<branch-or-company>
Acumatica__Username=<integration-user>
Acumatica__Password=<integration-password>
Acumatica__EndpointName=<endpoint-name>
Acumatica__EndpointVersion=<endpoint-version>
```

Do not commit real values. Use `.env` locally and managed secrets in hosted environments.

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

## Refresh Candidates

Current QA slice:

- `POST /api/invoices/refresh` imports the sanitized fixture at `backend/src/Pvm.Api/Features/Invoices/Fixtures/shoprite-invoice-basic.json`.
- The fixture creates invoice `INV342699282`.
- The fixture intentionally carries an unverified UOM warning, which is allowed in QA/staging.

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

- status is `Ready`
- `canSubmit` is `true`
- validation shows warning `unverified-shoprite-uom`
- generated XML is visible on the candidate detail page

## Validate Candidate Detail

API:

```powershell
$candidate = (Invoke-RestMethod -Uri http://localhost:5000/api/invoices/candidates)[0]
Invoke-RestMethod -Uri "http://localhost:5000/api/invoices/candidates/$($candidate.id)"
```

Workbench detail page should show:

- Acumatica invoice ID
- Shoprite PO number
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

Current local slice:

- `POST /api/invoices/{id}/submit` calls the submission command path.
- The registered local client returns a deterministic accepted response.
- This proves the local command, persistence, attempt history, and duplicate blocking path.

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
- confirm headers include `Authorization`, `ContractID`, `UIUser`, and `Accept: application/xml`
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
- Real Shoprite QA client is implemented in infrastructure but the API currently uses a local stub client for this slice.
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
