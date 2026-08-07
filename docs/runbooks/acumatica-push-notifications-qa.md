# Acumatica Invoice Push Notifications QA

This runbook configures the low-latency invoice-change trigger for QA. The
notification is only a hint: the worker always retrieves the current invoice
through Acumatica Contract REST before changing a candidate. Keep
`Automation__Mode=Disabled` throughout this procedure.

## Deployed Contract

- Endpoint: `POST https://<api-host>/api/webhooks/acumatica/invoice-changes`
- Content type: `application/json`
- Header: `X-PVM-Acumatica-Webhook-Secret`
- Allowed company: `PVM`
- Allowed query: `PVM-Shoprite-Finalized-Invoices`
- Body limit: 64 KiB
- Rate limit: 120 requests per minute per source address
- Key Vault secret: `acumatica--webhooksecret`

The endpoint returns `202 Accepted` only after the event-inbox record and all
invoice-discovery outbox messages commit in one PostgreSQL transaction. Repeated
delivery of the same environment, company, query, and Acumatica transaction ID
is recorded as a duplicate and does not enqueue another command.

## Generic Inquiry

Create this in Acumatica QA on **Generic Inquiries (SM208000)**.

Summary:

| Setting | Value |
| --- | --- |
| Inquiry title | `PVM-Shoprite-Finalized-Invoices` |
| Primary table | `PX.Objects.AR.ARInvoice` |
| Purpose | Header-only change detection for released sales invoices |

Use these result columns and aliases:

| Data field | Alias | Required by receiver |
| --- | --- | --- |
| `ARInvoice.NoteID` | `InvoiceId` | Yes |
| `ARInvoice.DocType` | `InvoiceType` | Diagnostic |
| `ARInvoice.RefNbr` | `ReferenceNbr` | Diagnostic |
| `ARInvoice.Status` | `Status` | Diagnostic |
| `ARInvoice.Released` | `Released` | Diagnostic |
| `ARInvoice.CustomerID` | `CustomerAccount` | Diagnostic only |
| `ARInvoice.CustomerOrderNbr` | `CustomerOrder` | Diagnostic only |
| `ARInvoice.LastModifiedDateTime` | `LastModifiedDateTime` | Diagnostic |

Conditions:

1. Document type is **Invoice**. Select the Acumatica selector value rather
   than typing an unverified internal code.
2. Released is `True`.
3. Voided is `False`.

Do not add detail lines, tax joins, aggregation, grouping, or formulas. The
receiver only requires `InvoiceId`; Contract REST remains authoritative for
status, customer eligibility, PO reference, lines, tax, and totals.

Before activating the push destination, run the inquiry in Acumatica and confirm:

- each invoice appears once;
- `InvoiceId` is present and stable;
- changing/finalizing a QA invoice changes the inquiry result;
- the query name in a test notification exactly matches the allowed value.

## Push Destination

Create this on **Push Notifications (SM302000)**.

| Setting | Value |
| --- | --- |
| Destination name | `PVM-Shoprite-Invoice-Webhook-QA` |
| Destination type | `Webhook` |
| Address | `https://ca-pvm-api-qa.blackbay-85d5b3d6.southafricanorth.azurecontainerapps.io/api/webhooks/acumatica/invoice-changes` |
| Header name | `X-PVM-Acumatica-Webhook-Secret` |
| Header value | Current `acumatica--webhooksecret` value from Key Vault |
| Generic inquiry | `PVM-Shoprite-Finalized-Invoices` |

Acumatica 2025 R2 explicitly supports one configured HTTP header name/value for
Webhook destinations. Do not put the secret in the URL. Keep the destination
inactive until the synthetic endpoint checks below pass.

Official references:

- [Push Notifications (SM302000)](https://help-2025r2.acumatica.com/%28W%285%29%29/Wiki/ShowWiki.aspx?pageid=ba35054f-3485-415e-9785-da1195cb708b)
- [Push Notification Format](https://help-2025r2.acumatica.com/Wiki/ShowWiki.aspx?PageID=7dabea03-649c-4228-8d5b-16e907be2c5a&wikiname=HelpRoot_Dev_Integration)

## Synthetic Endpoint Checks

Retrieve the QA secret into memory without printing it:

```powershell
$secret = az keyvault secret show `
  --vault-name kv-pvm-intg-qa `
  --name acumatica--webhooksecret `
  --query value -o tsv
```

Create a payload with a real QA Acumatica invoice `NoteID`:

```powershell
$transactionId = [guid]::NewGuid()
$payload = @{
  Inserted = @(@{
    InvoiceId = '<REAL-QA-NOTE-ID>'
    ReferenceNbr = '<REAL-QA-INVOICE-NUMBER>'
  })
  Deleted = @()
  Query = 'PVM-Shoprite-Finalized-Invoices'
  CompanyId = 'PVM'
  Id = $transactionId
  TimeStamp = [DateTime]::UtcNow.Ticks
  AdditionalInfo = @{}
} | ConvertTo-Json -Depth 5
```

Send it:

```powershell
$headers = @{ 'X-PVM-Acumatica-Webhook-Secret' = $secret }
$response = Invoke-RestMethod `
  -Method Post `
  -Uri 'https://ca-pvm-api-qa.blackbay-85d5b3d6.southafricanorth.azurecontainerapps.io/api/webhooks/acumatica/invoice-changes' `
  -Headers $headers `
  -ContentType 'application/json' `
  -Body $payload
$response
Remove-Variable secret
```

Expected:

1. First request: `202`, `duplicate=false`, `enqueued=1`.
2. Repeat the exact body: `202`, `duplicate=true`, `enqueued=0`.
3. `/admin/acumatica-events` shows one event with one duplicate.
4. `/admin/messages` shows one `acumatica.invoice-changed.v1` message published
   once and completed once.
5. The invoice candidate is created or updated from the authoritative REST
   response, not from the notification row.
6. Service Bus active and dead-letter counts return to zero.

Negative checks:

- missing or incorrect header returns `401`;
- wrong company/query returns `400`;
- malformed JSON returns `400`;
- payload over 64 KiB returns `413`;
- more than 120 requests per minute from one address returns `429`.

## Acumatica-Originated Check

1. Activate `PVM-Shoprite-Invoice-Webhook-QA`.
2. Finalize or update one eligible QA Shoprite invoice.
3. Confirm a new event in **Webhook Events** with the real transaction ID.
4. Confirm the matching candidate is refreshed exactly once.
5. Deliver the same notification again if Acumatica exposes a retry action;
   confirm only the duplicate count changes.
6. Confirm no `shoprite.invoice-submit.v1` message or new submission operation
   exists.

## Recovery Check

1. Record the worker replica state and queue counts.
2. Scale the worker to zero for an approved short test; leave the API running.
3. Cause one QA invoice notification.
4. Confirm the endpoint returns `202`, the inbox/outbox records exist, and the
   discovery queue retains the message.
5. Restore the worker to its previous scale.
6. Confirm one completed delivery, no dead letter, and an authoritative candidate
   refresh.
7. Confirm scheduled reconciliation still discovers an intentionally suppressed
   notification.

If Acumatica cannot deliver, inspect the destination history on **Push
Notifications (SM302000)** and the **System Monitor (SM201530)**. Do not delete
the PVM event-inbox, outbox, delivery, or run records during recovery.

## Rotation

1. Create a replacement random secret of at least 32 characters.
2. Update `acumatica--webhooksecret` in Key Vault.
3. Deploy the API revision so its secret reference changes.
4. Update the Header Value in Acumatica.
5. Send a synthetic check and then activate the destination.
6. Never print, commit, email, or place the secret in a URL.
