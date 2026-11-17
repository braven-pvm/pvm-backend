# Document capture: design

Date: 2026-11-17.
Status: draft for owner approval.

## 1. Intent

PVM needs the proof-of-delivery (POD) document for each delivery. When a client
queries a delivery, staff must find the POD for that invoice quickly. Today the
PODs are paper.

The project builds a document capture platform:

- An Android phone app is a "super scanner". It finds the page edges, crops the
  pages, and uploads the document. It does no processing.
- The backend reads each document, extracts the fields, matches the document to
  an Acumatica record, and files the document.
- A different user later recalls the document by the Acumatica reference. For a
  POD, the reference is the invoice number.

The POD is the first document type. Picking slips and other documents follow
later. The design must make a new document type a small addition, not a new
project.

## 2. Decisions from the owner

These decisions come from the owner in the design conversation:

1. The phone app uses Flutter, and the team side-loads it on Android phones.
2. Office staff scan the PODs in batches when the drivers bring them back. A
   small number of phones on office Wi-Fi do the scans. Drivers do not scan.
3. The POD papers are a mix: PVM's own signed invoice, and client delivery notes
   or goods-received notes. The invoice number is not always on the paper.
4. One POD always relates to one invoice.
5. The backend files each document in two places: backend blob storage, and an
   attachment on the matched record in Acumatica.
6. The phone is a capture device only. All reading, extraction, and matching
   occur on the backend.
7. A person fixes a document that the backend cannot match on a review list on
   the workbench, not on the phone.
8. Claude vision reads the documents (approach A in section 5).

## 3. Goals

1. Office staff scan a batch of PODs quickly, one after the other, without a
   wait for each result.
2. The backend files most PODs without a person.
3. Each POD that the backend cannot match safely goes to a review list. No scan
   is lost.
4. A user finds the POD for an invoice by invoice number on the workbench, and
   on the invoice record in Acumatica.
5. A new document type needs only a type definition and its match rules. The
   upload, storage, queue, review list, and recall stay the same.

## 4. Non-goals and anti-goals

Non-goals for the first release:

- Driver use, offline use in the field, and iOS.
- Document types other than POD. The platform supports them, but the first
  release defines POD only.
- Automatic detection of the document type. The user selects the type in the
  app.
- Play Store distribution and automatic app updates.

Anti-goals. These results are failures even if the software works:

- **A POD filed against the wrong invoice.** A wrong filing is worse than no
  filing. The backend files automatically only when exactly one Acumatica
  invoice matches with confidence. Every other case goes to the review list.
- **A lost scan.** The app keeps each scan until the backend confirms receipt.
  The backend keeps the original file before it starts any processing.
- **A slow batch.** The scanner must never wait for the read or the match.

## 5. Approaches

### 5.1 Where the processing occurs

| Approach | Trade-off | Result |
|---|---|---|
| Read and match on the phone, with ML Kit text recognition. | Fast feedback for the scanner, but each new document type needs phone work, and ML Kit reads handwriting badly. | Not selected. |
| Phone captures only. The backend reads, matches, and files. | The scanner gets no immediate result, but all logic is in one place, and new types need no app change. | **Selected by the owner.** |

### 5.2 How the backend reads a document

| Approach | Trade-off | Result |
|---|---|---|
| A. Claude vision extracts the fields for the document type as structured data. | Reads a mix of layouts and handwriting without training. A new type is a new field list. Approximately 1.5 US cents for each page. The page images go to Anthropic. | **Selected by the owner.** |
| B. Azure Document Intelligence reads the text, and backend rules find the numbers. | Data stays in Azure, and the cost is very low. Rules for each layout are fragile, and custom models need labelled samples. | Not selected. |
| C. Azure text read first, and Claude only when the rules find no match. | Lowest cost, but two read paths to build and maintain. | Not selected. Possible later if cost becomes important. |

A Microsoft Lens scan to a watched OneDrive folder was also considered. It has no
type selection, no upload confirmation, and no control over the file format, so
it does not fit a platform.

## 6. Design

### 6.1 Parts

```
Android app (Flutter)
  | 1. scan pages, make one PDF, upload with a client document ID
  v
Pvm.Api  POST /api/documents
  | 2. save the PDF to blob storage, insert a CapturedDocument row (Received)
  | 3. write a DocumentReceived message to the outbox
  v
Pvm.Worker  (Service Bus consumer)
  | 4. send the PDF to Claude with the field schema of the document type
  | 5. match the extracted values against Acumatica (backend code)
  | 6a. one safe match  -> attach the PDF to the Acumatica record -> Filed
  | 6b. other results   -> NeedsReview
  v
Workbench (Next.js)
  - Documents page: search and recall
  - Review list: a person selects the record -> the backend files the document
```

The backend parts follow the patterns that the repository already uses:

- The API, the auth policies, and the Entra sign-in in `Pvm.Api`.
- The outbox and the Service Bus consumer in `Pvm.Worker`.
- Blob storage, with the same approach as the payload archive in
  `Pvm.Infrastructure/PayloadArchive`.
- The Acumatica REST client in `Pvm.Infrastructure/Acumatica`.

### 6.2 Phone app

The scan must be as quick and easy as possible for a user with any level of
skill. This is a requirement from the owner. The papers have many sizes: mostly
A4 or square, but also long thin slips and large A3 papers. The scanner must
find the borders of each paper quickly.

- Flutter, Android only, in a new `mobile/` folder in this repository.
- The scan flow has the minimum number of taps:
  1. The user opens the app. The app remembers the sign-in and the last
     document type, and opens the camera immediately.
  2. The scanner finds the paper borders and captures the page automatically.
     The user can adjust the corners if necessary.
  3. The user taps "Add page" for a multi-page document, or "Done".
  4. The app queues the upload and opens the camera for the next document
     immediately.
- Large text and large buttons. No field to type in the scan flow.
- Before the build, a throwaway test app checks the ML Kit scanner on the real
  papers and phones: long thin slips, A3 papers, and poor light. If the ML Kit
  scanner fails on a paper shape, the builder selects a different edge
  detection, for example OpenCV, for that case.
- The scanner is Google's ML Kit document scanner, through the
  `google_mlkit_document_scanner` Flutter package. The scanner finds the page
  edges, corrects the perspective, crops each page, and lets the user add pages
  or scan again. It returns one PDF for the document.
- The user signs in with Microsoft Entra, with the same tenant as the workbench.
- Screens:
  1. Sign-in.
  2. Batch screen. The user selects the document type (only "POD" in the first
     release), then scans documents one after the other. A list shows each
     document in the batch with its upload state: waiting, uploading, received,
     or failed.
  3. Settings. This screen shows the signed-in user, the app version, and the
     backend address.
- The app gets the list of document types from the backend. A new type then
  needs no app release.
- Upload queue. The app keeps each PDF on the phone until the backend confirms
  receipt. If an upload fails, the app tries again with a delay. The app sends a
  client-generated document ID with each upload. The backend uses the ID to
  ignore a second copy of the same upload.
- The app does not show the read result or the match result.

### 6.3 Document types

A document type is a backend definition with these parts:

| Part | POD value |
|---|---|
| Code and name | `POD`, "Proof of delivery". |
| Field schema | The fields to extract (refer to 6.4). |
| Extraction instruction | The text for Claude that describes the papers and the fields. |
| Match strategy | A backend class that turns the extracted fields into Acumatica records. |
| Target entity | The Acumatica entity that gets the attachment: `SalesInvoice`. |

The first release keeps the definitions in code. A new type is a new definition
class and its match strategy.

### 6.4 Extraction

- The worker sends the PDF to Claude as a document block, with the extraction
  instruction and the field schema as structured output.
- POD fields: every invoice number on the page, every sales-order number, every
  client purchase-order number, the client name, the delivery date, whether a
  signature is present, and a free-text note for anything unclear. Each number
  field is a list, because the paper can show more than one candidate.
- Model: `claude-opus-5-5`. Effort and token limits are left to the builder.
- The worker keeps the full extraction result as JSON on the document row. The
  review list shows the result.

### 6.5 Match rules for POD

The invoice scope comes from the owner: all customers, and only Acumatica
documents of the type "Invoice". Debit memos, credit memos, and cash sales are
out of scope. My call: the status of the invoice is not a filter, except that
voided or cancelled invoices are out of scope. This lookup is new. The current
`AcumaticaInvoiceClient` reads only the Shoprite customers.

The match is backend code, not a model decision:

1. For each extracted invoice number, look up the invoice in Acumatica.
2. For each sales-order number and client purchase-order number, look up the
   related invoices in Acumatica. The client purchase-order number is on the
   sales order as the customer order number.
3. Collect the distinct invoices from steps 1 and 2.
4. If more than one invoice is found, for example a sales order with split
   shipments, the backend keeps only the invoices whose date is on or before
   the extracted delivery date and nearest to it. If this leaves one invoice,
   step 5 continues with that invoice.
5. The match is safe only if exactly one invoice remains and a second,
   independent signal agrees with it. A second signal is one of these:
   - The extracted client name agrees with the customer, the parent customer,
     or the ship-to name of the invoice. The comparison ignores case,
     punctuation, and common company suffixes.
   - Two different extracted numbers (for example an invoice number and a
     purchase-order number) point to the same invoice.
   A number alone is not sufficient. A client's own document number can be
   equal to a PVM invoice number by chance.
6. If the match is safe, the backend files the document.
7. In all other cases, the document goes to the review list with a reason: no
   candidate, more than one candidate, no second signal, client name conflict,
   or read failure. The review list shows the candidates that the backend
   found.

### 6.6 Filing

- Blob storage keeps the original PDF. The path contains the document ID, not
  the invoice number, so a correction needs no file move.
- The backend attaches the PDF to the invoice in Acumatica through the REST
  files endpoint of the `SalesInvoice` record. The file name contains the
  document type, the invoice number, and the document ID.
- Acumatica writes start behind a setting that is off, the same as the shadow
  mode of other integrations in this repository. With the setting off, the
  backend records the attachment that it would make. The owner turns the
  setting on after a check of the results.
- In shadow mode, a matched document has the state Filed, and the workbench
  recall works. The document row records that the Acumatica attachment is
  pending. When the owner turns on the setting, a backfill job attaches all
  pending documents.
- If a second POD arrives for an invoice that already has a POD, the backend
  files the second POD as well. The recall page shows both, newest first.

### 6.7 Document states

| State | Meaning |
|---|---|
| Received | The backend has the PDF and has queued it. |
| Processing | The worker reads and matches the document. |
| Filed | The document is attached to its Acumatica record. |
| NeedsReview | The document is on the review list, with a reason. |
| Failed | A technical failure after all retries. The document also shows on the review list. |

The backend records who uploaded the document, who resolved a review, and when
each change occurred.

### 6.8 Workbench

- **Documents page.** Search by invoice number, client, document type, state,
  and date. Each result shows the document, its state, and a link to the
  Acumatica record.
- **Review list.** Each entry shows the PDF, the extracted fields, and the
  reason. The user selects the correct invoice from a search, then files the
  document. The user can also reject a document, for example a bad scan, with a
  note.
- The pages follow `docs/design/design-system.md`.

### 6.9 Access

- A new auth policy `Documents.Capture` allows the upload. My proposal: the
  Operator and Admin roles get this policy. No new role is necessary.
- The phone app is a public client of the existing API app registration. It
  requests the same API scope as the workbench, so the existing app roles
  apply. The Entra change is a new mobile platform entry (redirect URI) for
  the app, which an Entra administrator must add.
- The Documents page and the review list use the current read and write
  policies.

## 7. Constraints

- The phones are Android only, and the app is side-loaded.
- The ML Kit document scanner needs Google Play services on the phone. Phones
  without Google Play services, for example recent Huawei phones, do not work.
- Volume: approximately 30 to 50 PODs each day. At this volume, the Claude cost
  is approximately 1 US dollar each day, and the worker needs no throttle.
- The owner supplies real POD samples for the read instruction and the tests.
- The POD page images go to Anthropic for the read. The owner accepted this with
  approach A.
- The Acumatica integration user needs permission to add files to sales
  invoices.
- The phone app sign-in needs an Entra app registration with a mobile platform
  entry.
- No secret goes into the app or the repository. The Anthropic API key goes into
  Key Vault, the same as the other secrets.

## 8. Calls made by the designer

The owner did not decide these items. They are proposals, and the owner can
change them:

1. Code location: `mobile/` in this repository, not a new repository.
2. The app makes one PDF for each document, and the backend keeps that PDF as
   the original.
3. The API key for Claude is a direct Anthropic API key in Key Vault, not
   Microsoft Foundry.
4. The model is `claude-opus-5-5`.
5. Auto-filing occurs only for exactly one safe match (refer to 6.5).
6. Acumatica attachments start in shadow mode (refer to 6.6).
7. A second POD for one invoice is kept, not rejected.
8. Documents stay in blob storage with no delete date.
9. Distribution: a signed release APK from a build pipeline, installed by hand.
10. The Operator and Admin roles can upload.
11. A safe match needs one invoice and a second signal (refer to 6.5).
12. Split shipments are narrowed by the delivery date, else they go to review.
13. In shadow mode, documents are Filed for recall, and a backfill job attaches
    them in Acumatica later (refer to 6.6).
14. The phone app uses the existing API app registration and roles (refer to
    6.9).

## 9. Left to the builder

- The database schema, the table names, and the migrations.
- The API routes and contracts, apart from the client document ID rule.
- The Flutter state management, the package choices apart from the scanner, and
  the screen layout.
- The retry counts and delays.
- The extraction prompt text, the effort level, and the token limits.
- The size limits and the compression for an upload.
- The tests. Tests must cover the match rules in 6.5 with real POD samples.
