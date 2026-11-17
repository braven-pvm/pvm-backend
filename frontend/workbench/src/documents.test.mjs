import assert from "node:assert/strict";
import { test } from "node:test";
import {
  buildDocumentSearchQuery,
  canActOnDocument,
  documentTypeLabel,
  fileToErrorMessage,
  formatDocumentState,
  formatReviewReason,
  readExtraction,
  rejectErrorMessage,
} from "./documents.ts";

test("each document state has a plain label", () => {
  assert.equal(formatDocumentState("Received"), "Received");
  assert.equal(formatDocumentState("Processing"), "Processing");
  assert.equal(formatDocumentState("Filed"), "Filed");
  assert.equal(formatDocumentState("NeedsReview"), "Needs review");
  assert.equal(formatDocumentState("Failed"), "Failed");
});

test("a stuck document says processing did not finish, whatever its state", () => {
  assert.equal(formatDocumentState("Received", true), "Stuck — processing did not finish");
  assert.equal(formatDocumentState("Processing", true), "Stuck — processing did not finish");
});

test("an unknown state is shown as it came", () => {
  assert.equal(formatDocumentState("Archived"), "Archived");
});

test("each review reason has one plain label", () => {
  const expected = {
    NoCandidate: "No matching invoice found",
    MultipleCandidates: "More than one possible invoice",
    NoSecondSignal: "Only one number matched — needs a check",
    ClientNameConflict: "Client name does not match the invoice",
    ReadFailure: "The document could not be read",
  };
  for (const [reason, label] of Object.entries(expected)) {
    assert.equal(formatReviewReason(reason, "NeedsReview"), label, reason);
  }
});

test("the reason shows for Failed documents too", () => {
  assert.equal(formatReviewReason("ReadFailure", "Failed"), "The document could not be read");
});

test("the reason is hidden for documents that are not in review", () => {
  assert.equal(formatReviewReason("NoCandidate", "Filed"), null);
  assert.equal(formatReviewReason("NoCandidate", "Processing"), null);
  assert.equal(formatReviewReason("NoCandidate", "Received"), null);
});

test("no reason gives no label", () => {
  assert.equal(formatReviewReason(null, "NeedsReview"), null);
  assert.equal(formatReviewReason(undefined, "Failed"), null);
});

test("an empty search sends no parameters", () => {
  assert.equal(buildDocumentSearchQuery({}), "");
  assert.equal(buildDocumentSearchQuery({ invoice: "", client: "  ", type: "", state: "", from: "", to: "" }), "");
});

test("filled fields are trimmed and sent", () => {
  const query = new URLSearchParams(buildDocumentSearchQuery({ invoice: " INV001234 ", client: "Acme", type: "POD", state: "Filed" }));
  assert.equal(query.get("invoice"), "INV001234");
  assert.equal(query.get("client"), "Acme");
  assert.equal(query.get("type"), "POD");
  assert.equal(query.get("state"), "Filed");
  assert.equal(query.has("from"), false);
});

test("dates become South African day bounds, and the end date is included", () => {
  const query = new URLSearchParams(buildDocumentSearchQuery({ from: "2026-11-01", to: "2026-11-30" }));
  assert.equal(query.get("from"), "2026-11-01T00:00:00+02:00");
  // The API's "to" is exclusive, so the day after the end date is sent.
  assert.equal(query.get("to"), "2026-12-01T00:00:00+02:00");
});

test("a date that is not yyyy-mm-dd is not sent", () => {
  assert.equal(buildDocumentSearchQuery({ from: "yesterday", to: "2026-13-45" }), "");
});

test("page 1 is the default and is not sent", () => {
  assert.equal(buildDocumentSearchQuery({ page: 1 }), "");
  assert.equal(buildDocumentSearchQuery({ page: 3 }), "page=3");
  assert.equal(buildDocumentSearchQuery({ page: "2" }), "page=2");
  assert.equal(buildDocumentSearchQuery({ page: "abc" }), "");
});

test("the browser query keeps the dates as typed", () => {
  const query = new URLSearchParams(buildDocumentSearchQuery({ from: "2026-11-01", to: "2026-11-30" }, { forApi: false }));
  assert.equal(query.get("from"), "2026-11-01");
  assert.equal(query.get("to"), "2026-11-30");
});

test("only open, not stuck documents can be filed or rejected", () => {
  assert.equal(canActOnDocument({ state: "NeedsReview", stuck: false }), true);
  assert.equal(canActOnDocument({ state: "Failed", stuck: false }), true);
  assert.equal(canActOnDocument({ state: "Filed", stuck: false }), false);
  assert.equal(canActOnDocument({ state: "Processing", stuck: true }), false);
  assert.equal(canActOnDocument({ state: "Received", stuck: true }), false);
});

test("extracted fields are read whatever their case", () => {
  const extraction = { InvoiceNumbers: ["INV1", "INV2"], clientname: "Acme", signaturePresent: true };
  assert.deepEqual(readExtraction(extraction, "invoiceNumbers"), ["INV1", "INV2"]);
  assert.equal(readExtraction(extraction, "clientName"), "Acme");
  assert.equal(readExtraction(extraction, "signaturePresent"), true);
  assert.equal(readExtraction(extraction, "deliveryDate"), undefined);
  assert.equal(readExtraction(null, "clientName"), undefined);
});

test("file-to errors tell the user what happened", () => {
  assert.match(fileToErrorMessage(409, "INV1"), /someone else already resolved/i);
  assert.match(fileToErrorMessage(400, "INV1"), /INV1 was not found/);
  assert.match(fileToErrorMessage(404, "INV1"), /no longer exists/i);
  assert.match(fileToErrorMessage(500, "INV1"), /nothing was filed/i);
});

test("reject errors tell the user what happened", () => {
  assert.match(rejectErrorMessage(409), /someone else already resolved/i);
  assert.match(rejectErrorMessage(400), /note/i);
});

test("document types have a name", () => {
  assert.equal(documentTypeLabel("POD"), "Proof of delivery");
  assert.equal(documentTypeLabel("XYZ"), "XYZ");
});
