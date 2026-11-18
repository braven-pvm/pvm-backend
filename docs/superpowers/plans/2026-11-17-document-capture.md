# Document Capture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An Android scanner app uploads documents, and the backend reads, matches, and files each proof of delivery (POD) against its Acumatica invoice, with a workbench review list and recall.

**Architecture:** The Flutter app captures one PDF for each document and uploads it to `Pvm.Api`. The API stores the PDF in blob storage, inserts a `CapturedDocument` row, and queues a message through the existing outbox. `Pvm.Worker` sends the PDF to Claude for field extraction, matches the fields to an Acumatica invoice with fixed rules, and files the document or puts it on the review list. The workbench shows search, recall, and the review list.

**Tech Stack:** .NET (existing `backend/`), EF Core, Azure Blob Storage, Azure Service Bus, Anthropic C# SDK (`Anthropic` NuGet package), Next.js workbench (existing `frontend/workbench`), Flutter with `google_mlkit_document_scanner` and `msal_auth`.

**Spec:** `docs/superpowers/specs/2026-11-17-document-capture-design.md`

## Global Constraints

- Only Acumatica documents of the type "Invoice" are match candidates. Voided and cancelled invoices are out of scope. All customers are in scope.
- Auto-filing needs exactly one invoice and a second signal (spec 6.5). A number alone is never sufficient.
- Acumatica attachments start in shadow mode: setting `DocumentCapture:AcumaticaAttachMode` = `Shadow` (other value: `Write`).
- Document states: `Received`, `Processing`, `Filed`, `NeedsReview`, `Failed`. Review reasons: `NoCandidate`, `MultipleCandidates`, `NoSecondSignal`, `ClientNameConflict`, `ReadFailure`. A second POD for one invoice is filed, not reviewed.
- The open review list shows documents in `NeedsReview` and `Failed` that are not rejected.
- Model: `claude-opus-5-5`. The API key comes from Key Vault secret `Anthropic--ApiKey`. No secret in the app or the repository.
- Blob path: `documents/{documentType}/{yyyy}/{MM}/{documentId}.pdf`. Never the invoice number.
- Acumatica file name: `{documentType}-{invoiceNumber}-{documentId}.pdf`.
- Upload auth policy: `Documents.Capture` = roles Admin and Operator. Read pages: `Invoices.Read`. Review actions: `Invoices.Write`.
- The client document ID (a GUID from the app) makes an upload idempotent.
- Board rules: each change below is one branch from `main`, reviewed with `board_review`, and landed before the next change that depends on it.

## Review Focus

1. **The same upload sent twice** (a retry after a timeout). Expected: one document row, and the second call returns the first document. Test in Task 3.
2. **A client document number equal to a PVM invoice number by chance.** Expected: review with `NoSecondSignal`, never auto-file. Test in Task 2.
3. **Claude returns a refusal, a malformed result, or an empty field list.** Expected: `NeedsReview` with `ReadFailure`, and the PDF stays recallable. Test in Task 5.
4. **An Acumatica attach call fails in Write mode.** Expected: the document stays `Filed` with the attachment marked pending, and the backfill retries it. Test in Task 6.
5. **The phone loses the network during a batch.** Expected: the queued PDFs survive an app restart and upload later. Test in Task 9.

---

## Change A: scanner test app (throwaway)

### Task 1: ML Kit scanner test app

Branch: `spike/document-scanner`. This branch never lands. It answers one question: does the ML Kit scanner capture the real papers on the real phones?

**Files:**
- Create: `mobile/spike_scanner/` (Flutter app, `flutter create`)

- [ ] **Step 1:** Create the app with `flutter create --platforms=android mobile/spike_scanner`, add `google_mlkit_document_scanner`, set `minSdkVersion` 21.
- [ ] **Step 2:** Make one screen with one large "Scan" button. The button starts `DocumentScanner` with `DocumentFormat.pdf` and `DocumentFormat.jpeg`, `ScannerMode.full`, `isGalleryImport: false`, `pageLimit: 10`. Show the page count and the PDF size, and share the PDF with the Android share sheet.
- [ ] **Step 3:** Build a release APK: `flutter build apk --release`.
- [ ] **Step 4:** The owner installs the APK and scans these papers: A4, square, a long thin slip, A3, a creased paper, and a paper in poor light. Record for each paper: edge detected (yes or no), the corner fix needed (yes or no), the text readable in the PDF (yes or no).
- [ ] **Step 5:** Write the result in `docs/superpowers/specs/2026-11-17-document-capture-design.md` section 6.2 as a short table. If a paper shape fails, stop and bring the result to the owner before Task 9.

---

## Change B: backend document model and match rules

Branch: `feature/document-capture-core`.

### Task 2: Document types and the POD matcher

**Files:**
- Create: `backend/src/Pvm.Application/Documents/DocumentModels.cs`
- Create: `backend/src/Pvm.Application/Documents/IDocumentType.cs`
- Create: `backend/src/Pvm.Application/Documents/ProofOfDeliveryDocumentType.cs`
- Create: `backend/src/Pvm.Application/Documents/PodMatcher.cs`
- Create: `backend/src/Pvm.Application/Documents/CompanyNameComparer.cs`
- Test: `backend/tests/Pvm.Application.Tests/Documents/PodMatcherTests.cs`
- Test: `backend/tests/Pvm.Application.Tests/Documents/CompanyNameComparerTests.cs`

**Interfaces:**
- Produces:
  - `enum DocumentState { Received, Processing, Filed, NeedsReview, Failed }`
  - `enum ReviewReason { NoCandidate, MultipleCandidates, NoSecondSignal, ClientNameConflict, ReadFailure }`
  - `record PodExtraction(IReadOnlyList<string> InvoiceNumbers, IReadOnlyList<string> SalesOrderNumbers, IReadOnlyList<string> CustomerOrderNumbers, string? ClientName, DateOnly? DeliveryDate, bool SignaturePresent, string? Note)`
  - `record InvoiceCandidate(string ReferenceNumber, DateOnly Date, string CustomerId, string CustomerName, string? ParentCustomerName, string? ShipToName, string Status)`
  - `record CandidateHit(InvoiceCandidate Invoice, string MatchedOn)` where `MatchedOn` is `"invoice"`, `"salesOrder"`, or `"customerOrder"`.
  - `abstract record MatchOutcome` with `sealed record Matched(InvoiceCandidate Invoice) : MatchOutcome` and `sealed record Review(ReviewReason Reason, IReadOnlyList<InvoiceCandidate> Candidates) : MatchOutcome`
  - `static MatchOutcome PodMatcher.Decide(PodExtraction extraction, IReadOnlyList<CandidateHit> hits)`
  - `static bool CompanyNameComparer.Agrees(string extracted, InvoiceCandidate invoice)`
  - `interface IDocumentType { string Code; string DisplayName; string AcumaticaEntity; JsonElement FieldSchema; string ExtractionInstruction; }`
  - `ProofOfDeliveryDocumentType : IDocumentType` with `Code = "POD"`, `DisplayName = "Proof of delivery"`, `AcumaticaEntity = "SalesInvoice"`.

- [ ] **Step 1: Write the failing tests** in `PodMatcherTests.cs`:
  - `NoHits_ReviewNoCandidate`: empty hits → `Review(NoCandidate, [])`.
  - `OneInvoiceByNumberOnly_NoClientName_ReviewNoSecondSignal`: one hit on `"invoice"`, `ClientName = null` → `Review(NoSecondSignal, [that invoice])`.
  - `OneInvoiceByNumber_ClientNameAgrees_Matched`.
  - `OneInvoice_HitByInvoiceAndCustomerOrder_Matched`: two hits with different `MatchedOn` for the same invoice, no client name → `Matched`.
  - `OneInvoice_ClientNameDisagrees_ReviewClientNameConflict`.
  - `TwoInvoices_DeliveryDateSelectsNearestOnOrBefore_Matched`: invoices dated 2026-11-10 and 2026-11-14, delivery 2026-11-12, client agrees → `Matched` with the 2026-11-10 invoice.
  - `TwoInvoices_NoDeliveryDate_ReviewMultipleCandidates`.
  - `VoidedOrCancelledInvoice_IsIgnored`: a hit with `Status = "Voided"` or `"Canceled"` counts as no hit.
- [ ] **Step 2: Write the failing tests** in `CompanyNameComparerTests.cs`: `"SHOPRITE CHECKERS (PTY) LTD"` agrees with customer `"Shoprite Checkers Pty Ltd"`; `"Checkers Hyper Brackenfell"` agrees with ship-to `"CHECKERS HYPER BRACKENFELL"`; `"Pick n Pay"` does not agree with `"Shoprite Checkers"`; a parent customer name agrees.
- [ ] **Step 3: Run** `dotnet test backend/tests/Pvm.Application.Tests --filter "FullyQualifiedName~Documents"`. Expected: FAIL, types not defined.
- [ ] **Step 4: Implement** the types above. `CompanyNameComparer` normalises to lower case, deletes punctuation, and deletes the suffixes `pty`, `ltd`, `limited`, `cc`, `inc`, `t/a`. Two names agree if one normalised name contains the other. `PodMatcher.Decide` follows spec 6.5 steps 3 to 7 in that order. The POD field schema has `additionalProperties: false`, and all fields in `PodExtraction` are required (lists may be empty, strings nullable).
- [ ] **Step 5: Run** the same command. Expected: PASS.
- [ ] **Step 6: Commit** `feat(documents): POD document type and match rules`.

---

## Change C: intake API

Branch: `feature/document-capture-intake`. Depends on Change B.

### Task 3: Document entity, blob store, and upload endpoint

**Files:**
- Create: `backend/src/Pvm.Infrastructure/Persistence/Entities/CapturedDocumentEntity.cs`
- Modify: `backend/src/Pvm.Infrastructure/Persistence/PvmDbContext.cs` (add `DbSet<CapturedDocumentEntity> CapturedDocuments`, unique index on `ClientDocumentId`, index on `MatchedReference`)
- Create: migration `AddCapturedDocuments` under `backend/src/Pvm.Infrastructure/Persistence/Migrations/`
- Create: `backend/src/Pvm.Application/Documents/IDocumentStore.cs`
- Create: `backend/src/Pvm.Infrastructure/Documents/BlobDocumentStore.cs`
- Create: `backend/src/Pvm.Infrastructure/Documents/DocumentCaptureOptions.cs`
- Create: `backend/src/Pvm.Infrastructure/Documents/ServiceCollectionExtensions.cs`
- Create: `backend/src/Pvm.Api/Features/Documents/DocumentIntakeEndpoints.cs`
- Modify: `backend/src/Pvm.Api/Auth/ServiceCollectionExtensions.cs` (policy `Documents.Capture` = `AppRoles.Write`)
- Modify: `backend/src/Pvm.Application/Messaging/IntegrationMessageContracts.cs` (queue `document-processing`, type `document.received.v1`, `record ProcessCapturedDocumentMessage(Guid DocumentId)`)
- Modify: `infra/azure/` Service Bus module (queue `document-processing`) and a blob container `documents`
- Test: `backend/tests/Pvm.Api.Tests/Documents/DocumentIntakeEndpointsTests.cs`
- Test: `backend/tests/Pvm.Infrastructure.Tests/Documents/BlobDocumentStoreTests.cs`

**Interfaces:**
- Consumes: `DocumentState`, `IDocumentType` (Task 2).
- Produces:
  - `CapturedDocumentEntity { Guid Id; Guid ClientDocumentId; string DocumentType; DocumentState State; ReviewReason? ReviewReason; string BlobPath; int PageCount; long SizeBytes; string Sha256; string? ExtractionJson; string? MatchedReference; string? MatchedCustomerName; string? MatchedBy; bool AcumaticaAttachPending; DateTimeOffset? AcumaticaAttachedAt; string UploadedBy; DateTimeOffset UploadedAt; string? ResolvedBy; DateTimeOffset? ResolvedAt; string? ReviewNote; DateTimeOffset UpdatedAt; }`
  - `IDocumentStore.SaveAsync(string blobPath, Stream pdf, CancellationToken) -> Task<StoredDocument(string Sha256, long SizeBytes)>` and `OpenReadAsync(string blobPath, CancellationToken) -> Task<Stream>`
  - `POST /api/documents` multipart: `file` (PDF), `clientDocumentId` (GUID), `documentType` (string), `pageCount` (int). Returns `201` with `{ documentId, state }`, or `200` with the same body for a known `clientDocumentId`.
  - `GET /api/documents/types` returns `[{ code, displayName }]`.

- [ ] **Step 1: Write the failing tests:**
  - `Upload_NewDocument_Returns201_SavesBlob_InsertsReceivedRow_QueuesMessage`.
  - `Upload_SameClientDocumentIdTwice_Returns200_SameDocumentId_OneRow_OneMessage` (Review Focus 1).
  - `Upload_NotPdf_Returns400` (the file must start with `%PDF-`).
  - `Upload_OverSizeLimit_Returns413` (limit from `DocumentCaptureOptions.MaxUploadBytes`, default 25 MB).
  - `Upload_UnknownDocumentType_Returns400`.
  - `Upload_ViewerRole_Returns403`.
  - `BlobDocumentStore_Save_ThenOpenRead_ReturnsSameBytes` (uses the storage emulator, same as the payload archive tests).
- [ ] **Step 2: Run** `dotnet test backend/tests/Pvm.Api.Tests --filter "FullyQualifiedName~Documents"`. Expected: FAIL.
- [ ] **Step 3: Implement** the entity, the migration, the store, and the endpoints. The endpoint writes the blob, the row, and the outbox message in that order, and the row insert and the outbox write share one transaction. Use the existing `IIntegrationCommandQueue.EnqueueAsync` with correlation ID = document ID.
- [ ] **Step 4: Run** the tests. Expected: PASS.
- [ ] **Step 5: Commit** `feat(documents): document upload endpoint and storage`.

---

## Change D: processing in the worker

Branch: `feature/document-capture-processing`. Depends on Change C.

### Task 4: Acumatica invoice lookup and attachment for documents

**Files:**
- Create: `backend/src/Pvm.Application/Documents/IAcumaticaDocumentClient.cs`
- Create: `backend/src/Pvm.Infrastructure/Acumatica/AcumaticaDocumentClient.cs`
- Test: `backend/tests/Pvm.Infrastructure.Tests/Acumatica/AcumaticaDocumentClientTests.cs`

**Interfaces:**
- Consumes: `InvoiceCandidate`, `CandidateHit` (Task 2), the sign-in and URI helpers of `AcumaticaInvoiceClient`.
- Produces:
  - `Task<IReadOnlyList<CandidateHit>> FindInvoicesAsync(PodExtraction extraction, CancellationToken)`
  - `Task<IReadOnlyList<InvoiceCandidate>> SearchInvoicesAsync(string query, CancellationToken)` (for the review list)
  - `Task AttachFileAsync(string entity, string referenceNumber, string fileName, Stream pdf, CancellationToken)`

- [ ] **Step 1: Write the failing tests** with a fake `HttpMessageHandler`:
  - `FindInvoices_ByInvoiceNumber_FiltersTypeInvoice` (the request filter contains `Type eq 'Invoice'` and no customer filter).
  - `FindInvoices_ByCustomerOrder_LooksUpSalesOrdersThenInvoices`.
  - `FindInvoices_DeduplicatesTheSameInvoiceFromTwoNumbers` (keeps one `CandidateHit` for each `MatchedOn`).
  - `AttachFile_PutsToSalesInvoiceFilesEndpoint` (method PUT, path `entity/{endpoint}/{version}/SalesInvoice/Invoice/{ref}/files/{fileName}`).
  - `AttachFile_ServerError_Throws`.
- [ ] **Step 2: Run** `dotnet test backend/tests/Pvm.Infrastructure.Tests --filter "FullyQualifiedName~AcumaticaDocumentClient"`. Expected: FAIL.
- [ ] **Step 3: Implement** `AcumaticaDocumentClient`. Reuse the session sign-in and the same-server credential rule of `AcumaticaInvoiceClient`. Do not reuse its Shoprite customer filter.
- [ ] **Step 4: Run** the tests. Expected: PASS.
- [ ] **Step 5: Commit** `feat(documents): Acumatica invoice lookup and file attach`.

### Task 5: Claude extraction

**Files:**
- Modify: `backend/src/Pvm.Infrastructure/Pvm.Infrastructure.csproj` (add `Anthropic` package)
- Create: `backend/src/Pvm.Application/Documents/IDocumentExtractor.cs`
- Create: `backend/src/Pvm.Infrastructure/Documents/ClaudeDocumentExtractor.cs`
- Test: `backend/tests/Pvm.Infrastructure.Tests/Documents/ClaudeDocumentExtractorTests.cs`

**Interfaces:**
- Consumes: `IDocumentType`, `PodExtraction` (Task 2).
- Produces: `Task<ExtractionResult> ExtractAsync(IDocumentType type, Stream pdf, CancellationToken)` where `abstract record ExtractionResult` has `Extracted(JsonElement Fields)` and `Unreadable(string Reason)`.

- [ ] **Step 1: Write the failing tests** with a fake HTTP handler on the SDK client:
  - `Extract_ValidJson_ReturnsExtracted`.
  - `Extract_Refusal_ReturnsUnreadable` (Review Focus 3).
  - `Extract_MaxTokens_ReturnsUnreadable`.
  - `Extract_JsonFailsSchema_ReturnsUnreadable`.
  - `Extract_RequestSendsPdfAsDocumentBlock_AndSchemaAsOutputFormat`.
- [ ] **Step 2: Run** `dotnet test backend/tests/Pvm.Infrastructure.Tests --filter "FullyQualifiedName~ClaudeDocumentExtractor"`. Expected: FAIL.
- [ ] **Step 3: Implement** with the Anthropic C# SDK: `DocumentBlockParam` with `Base64PdfSource`, then a text block with `type.ExtractionInstruction`. `OutputConfig.Format = new JsonOutputFormat { Schema = type.FieldSchema }`. Model `claude-opus-5-5`, `OutputConfig.Effort = Effort.Low` as the start value. Use the server-side refusal fallback (`fallbacks: "default"`, beta `server-side-fallback-2026-07-01`). Check `StopReason` before the content is read. Read the API key from configuration key `Anthropic:ApiKey` (Key Vault secret `Anthropic--ApiKey`).
- [ ] **Step 4: Run** the tests. Expected: PASS.
- [ ] **Step 5: Commit** `feat(documents): Claude document extraction`.

### Task 6: Document processor, worker wiring, and backfill

**Files:**
- Create: `backend/src/Pvm.Infrastructure/Documents/DocumentProcessor.cs`
- Create: `backend/src/Pvm.Infrastructure/Documents/DocumentAttachBackfillService.cs`
- Modify: `backend/src/Pvm.Infrastructure/Messaging/IntegrationMessageProcessor.cs` (route `document-processing` / `document.received.v1` to `DocumentProcessor`)
- Modify: `backend/src/Pvm.Worker/Program.cs` (register the documents services and the backfill hosted service)
- Test: `backend/tests/Pvm.Infrastructure.Tests/Documents/DocumentProcessorTests.cs`

**Interfaces:**
- Consumes: Tasks 2, 3, 4, 5.
- Produces: `Task ProcessAsync(Guid documentId, CancellationToken)` and `Task<int> BackfillPendingAttachmentsAsync(CancellationToken)`.

- [ ] **Step 1: Write the failing tests:**
  - `Process_SafeMatch_ShadowMode_FiledWithAttachPending_NoAcumaticaCall`.
  - `Process_SafeMatch_WriteMode_FiledAndAttached`.
  - `Process_WriteMode_AttachFails_FiledWithAttachPending` (Review Focus 4).
  - `Process_Unreadable_NeedsReviewReadFailure`.
  - `Process_NoSecondSignal_NeedsReview_ExtractionSaved`.
  - `Process_AlreadyFiled_DoesNothing` (a second delivery of the same message).
  - `Process_AcumaticaLookupThrows_Rethrows_SoServiceBusRetries`. After the last delivery, the existing dead-letter path sets the row to `Failed`.
  - `Backfill_WriteMode_AttachesPendingDocuments_ClearsFlag`.
  - `Backfill_ShadowMode_DoesNothing`.
- [ ] **Step 2: Run** `dotnet test backend/tests/Pvm.Infrastructure.Tests --filter "FullyQualifiedName~DocumentProcessor"`. Expected: FAIL.
- [ ] **Step 3: Implement.** Order in `ProcessAsync`: set `Processing`, read the PDF, extract, save `ExtractionJson`, find hits, decide, then file or review. The backfill service runs every 15 minutes and does nothing in shadow mode.
- [ ] **Step 4: Run** the tests, then the full backend suite `dotnet test backend/Pvm.sln`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(documents): document processing in the worker`.

---

## Change E: recall and review

Branch: `feature/document-capture-workbench`. Depends on Change D.

### Task 7: Read and review endpoints

**Files:**
- Create: `backend/src/Pvm.Api/Features/Documents/DocumentReviewEndpoints.cs`
- Test: `backend/tests/Pvm.Api.Tests/Documents/DocumentReviewEndpointsTests.cs`

**Interfaces:**
- Produces:
  - `GET /api/documents?invoice=&client=&type=&state=&from=&to=&page=` (policy `Invoices.Read`), newest first.
  - `GET /api/documents/{id}` with the extraction and the candidates.
  - `GET /api/documents/{id}/file` streams the PDF.
  - `GET /api/documents/review/invoice-search?q=` (policy `Invoices.Write`), from `SearchInvoicesAsync`.
  - `POST /api/documents/{id}/file-to` body `{ invoiceReference }` (policy `Invoices.Write`): sets `Filed`, `MatchedBy = "user:{email}"`, `ResolvedBy`, `ResolvedAt`, and attaches or marks pending by the attach mode.
  - `POST /api/documents/{id}/reject` body `{ note }` (policy `Invoices.Write`): keeps `NeedsReview`, sets `ReviewNote`, and hides the document from the open review list.

- [ ] **Step 1: Write the failing tests:** search by invoice returns both PODs for one invoice, newest first; `file-to` on a `Filed` document returns 409; `file-to` with an invoice that the search cannot find returns 400; a Viewer gets 403 on `file-to`; the file endpoint returns `application/pdf`.
- [ ] **Step 2: Run** `dotnet test backend/tests/Pvm.Api.Tests --filter "FullyQualifiedName~DocumentReview"`. Expected: FAIL.
- [ ] **Step 3: Implement** the endpoints.
- [ ] **Step 4: Run** the tests. Expected: PASS.
- [ ] **Step 5: Commit** `feat(documents): document search and review endpoints`.

### Task 8: Workbench Documents and Review pages

**Files:**
- Create: `frontend/workbench/app/documents/page.tsx` (search and recall)
- Create: `frontend/workbench/app/documents/[id]/page.tsx` (one document: PDF, fields, history)
- Create: `frontend/workbench/app/documents/review/page.tsx` (review list)
- Create: `frontend/workbench/src/documents.ts` and `frontend/workbench/src/documents.test.mjs`
- Modify: `frontend/workbench/src/navigation.ts` (add "Documents")
- Modify: `frontend/workbench/scripts/mock-api.mjs` (document fixtures)

- [ ] **Step 1: Write the failing tests** in `documents.test.mjs` for the pure helpers: `formatDocumentState`, `formatReviewReason` (one label for each `ReviewReason`), and `buildDocumentSearchQuery` (empty fields are not sent).
- [ ] **Step 2: Run** `npm test` in `frontend/workbench`. Expected: FAIL.
- [ ] **Step 3: Implement** the helpers and the three pages with the components in `docs/design/design-system.md`. The review page shows the PDF next to the extracted fields and the candidates, with an invoice search box and the buttons "File to this invoice" and "Reject".
- [ ] **Step 4: Run** `npm test`, `npm run lint`, and `npm run build`. Expected: PASS. Take screenshots of the three pages against the mock API for the board review.
- [ ] **Step 5: Commit** `feat(workbench): documents search and review pages`.

---

## Change F: the scanner app

Branch: `feature/document-capture-mobile`. Depends on Change C (the API) and the result of Task 1.

### Task 9: Flutter scanner app

**Files:**
- Create: `mobile/scanner/` (Flutter app, Android only)
- Key files: `lib/main.dart`, `lib/auth/auth_service.dart`, `lib/api/documents_api.dart`, `lib/queue/upload_queue.dart`, `lib/screens/batch_screen.dart`, `lib/screens/settings_screen.dart`
- Test: `mobile/scanner/test/upload_queue_test.dart`, `mobile/scanner/test/documents_api_test.dart`

**Interfaces:**
- Consumes: `POST /api/documents`, `GET /api/documents/types` (Task 3).
- Produces: `UploadQueue.enqueue(File pdf, String documentType, int pageCount) -> Future<QueuedDocument>`, `UploadQueue.items -> Stream<List<QueuedDocument>>`, `QueuedDocument { String clientDocumentId; String state; }` where `state` is `waiting`, `uploading`, `received`, or `failed`.

- [ ] **Step 1: Write the failing tests** in `upload_queue_test.dart`:
  - `enqueue_persists_item_and_file_across_restart` (Review Focus 5: a new `UploadQueue` on the same directory sees the item).
  - `upload_success_marks_received_and_deletes_local_pdf`.
  - `upload_failure_retries_with_backoff_and_keeps_pdf`.
  - `retry_sends_same_clientDocumentId`.
- [ ] **Step 2: Run** `flutter test` in `mobile/scanner`. Expected: FAIL.
- [ ] **Step 3: Implement** the queue (files in the app documents folder, item list as JSON), the API client (bearer token from `msal_auth`), and the screens. The batch screen opens the scanner immediately after sign-in and after each document (spec 6.2 scan flow). Large buttons. No text input in the scan flow. The backend address and the Entra client ID come from `--dart-define` values at build time.
- [ ] **Step 4: Run** `flutter test` and `flutter build apk --release`. Expected: PASS and an APK.
- [ ] **Step 5:** Test on a phone against QA: scan 5 papers, then confirm 5 rows in the workbench Documents page.
- [ ] **Step 6: Commit** `feat(mobile): document scanner app`.

---

## Owner actions

These steps need the owner or an administrator. Post each one to the board as an `action` item at the start of the change that needs it:

1. Change D: add the Key Vault secret `Anthropic--ApiKey` for QA and production.
2. Change D: give the Acumatica integration user permission to read sales orders and to add files to sales invoices.
3. Change F: add an Android platform redirect URI for the scanner app to the existing API app registration in Entra.
4. Change F: decide how the APK goes to the office phones (the plan assumes a manual install of a signed APK).
5. After a production check of shadow results: set `DocumentCapture:AcumaticaAttachMode` to `Write`.
6. Supply 10 to 20 real POD samples before Task 5, for the extraction instruction and the tests.
