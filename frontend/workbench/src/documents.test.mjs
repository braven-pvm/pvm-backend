import assert from "node:assert/strict";
import { test } from "node:test";
import {
  acumaticaInvoiceUrl,
  buildDocumentSearchQuery,
  isDocumentId,
  canActOnDocument,
  canProcessAgain,
  canReturnToReview,
  deleteErrorMessage,
  documentDeletedMessage,
  documentTypeLabel,
  fileToAttachText,
  fileToErrorMessage,
  formatDocumentState,
  formatReviewReason,
  readExtraction,
  processAgainErrorMessage,
  rejectErrorMessage,
  returnToReviewErrorMessage,
  showDeleteAction,
  unconfirmedMessage,
  uploaderText,
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

test("the Acumatica link opens the invoice screen with the encoded reference", () => {
  assert.equal(
    acumaticaInvoiceUrl("https://pvm.acumatica.com", "INV001234"),
    "https://pvm.acumatica.com/Main?ScreenId=AR301000&DocType=INV&RefNbr=INV001234",
  );
  assert.equal(
    acumaticaInvoiceUrl("https://pvm.acumatica.com/erp/", " INV 12/3&x "),
    "https://pvm.acumatica.com/erp/Main?ScreenId=AR301000&DocType=INV&RefNbr=INV%2012%2F3%26x",
  );
});

test("no setting or no reference gives no link", () => {
  assert.equal(acumaticaInvoiceUrl(undefined, "INV001234"), null);
  assert.equal(acumaticaInvoiceUrl("", "INV001234"), null);
  assert.equal(acumaticaInvoiceUrl("https://pvm.acumatica.com", null), null);
  assert.equal(acumaticaInvoiceUrl("https://pvm.acumatica.com", "  "), null);
});

test("a setting that is not an https URL is ignored", () => {
  assert.equal(acumaticaInvoiceUrl("http://pvm.acumatica.com", "INV001234"), null);
  assert.equal(acumaticaInvoiceUrl("javascript:alert(1)", "INV001234"), null);
  assert.equal(acumaticaInvoiceUrl("pvm.acumatica.com", "INV001234"), null);
});

test("document ids are GUIDs", () => {
  assert.equal(isDocumentId("d0c00000-0000-4000-8000-000000000001"), true);
  assert.equal(isDocumentId("../etc/passwd"), false);
  assert.equal(isDocumentId(""), false);
  assert.equal(isDocumentId(undefined), false);
});

test("an unconfirmed write tells the user to reload", () => {
  assert.equal(
    unconfirmedMessage("filed"),
    "We could not confirm whether the document was filed. Reload the page to see its state.",
  );
  assert.equal(
    unconfirmedMessage("rejected"),
    "We could not confirm whether the document was rejected. Reload the page to see its state.",
  );
});

test("only a filed document that is not attached yet can be sent back to review", () => {
  assert.equal(canReturnToReview({ state: "Filed", acumaticaAttachedAt: null }), true);
  assert.equal(canReturnToReview({ state: "Filed", acumaticaAttachedAt: "2026-11-17T10:00:00Z" }), false);
  assert.equal(canReturnToReview({ state: "NeedsReview", acumaticaAttachedAt: null }), false);
  assert.equal(canReturnToReview({ state: "Failed", acumaticaAttachedAt: null }), false);
});

test("a failed or stuck document can be processed again", () => {
  assert.equal(canProcessAgain({ state: "Failed", stuck: false }), true);
  assert.equal(canProcessAgain({ state: "Processing", stuck: true }), true);
  assert.equal(canProcessAgain({ state: "Received", stuck: true }), true);
  assert.equal(canProcessAgain({ state: "Processing", stuck: false }), false);
  assert.equal(canProcessAgain({ state: "NeedsReview", stuck: false }), false);
  assert.equal(canProcessAgain({ state: "Filed", stuck: false }), false);
});

test("the file-to dialog says what happens in Acumatica for the attach mode", () => {
  assert.equal(fileToAttachText("Write"), "The PDF will be attached to this invoice in Acumatica.");
  assert.equal(fileToAttachText("Shadow"), "The PDF will be attached in Acumatica when attachments are switched on.");
  // An unknown or missing mode never promises an attachment.
  assert.equal(fileToAttachText(undefined), "The PDF will be attached in Acumatica when attachments are switched on.");
});

test("send-back and process-again errors tell the user what happened", () => {
  assert.match(returnToReviewErrorMessage(409), /attached in Acumatica|no longer filed/i);
  assert.match(returnToReviewErrorMessage(400), /note/i);
  assert.match(returnToReviewErrorMessage(403), /permission/i);
  assert.match(processAgainErrorMessage(409), /failed or stuck/i);
  assert.match(processAgainErrorMessage(500), /nothing changed/i);
  assert.equal(
    unconfirmedMessage("sent back to review"),
    "We could not confirm whether the document was sent back to review. Reload the page to see its state.",
  );
});

test("a phone upload shows the device and the staff name", () => {
  assert.equal(
    uploaderText({ uploadedBy: "device:Dispatch phone 1", scannerDeviceName: "Dispatch phone 1", scannerStaffName: "Thabo" }),
    "Dispatch phone 1 · Thabo",
  );
});

test("a phone upload without a staff name shows the device only", () => {
  assert.equal(uploaderText({ uploadedBy: "device:Dispatch phone 1", scannerDeviceName: "Dispatch phone 1", scannerStaffName: null }), "Dispatch phone 1");
  assert.equal(uploaderText({ uploadedBy: "device:Dispatch phone 1", scannerDeviceName: "Dispatch phone 1", scannerStaffName: "  " }), "Dispatch phone 1");
});

test("a workbench upload shows the uploader as before", () => {
  assert.equal(uploaderText({ uploadedBy: "driver.thabo@pvm.co.za", scannerDeviceName: null, scannerStaffName: null }), "driver.thabo@pvm.co.za");
  assert.equal(uploaderText({ uploadedBy: "driver.thabo@pvm.co.za" }), "driver.thabo@pvm.co.za");
});

test("showDeleteAction follows the backend's canDelete, for Admin and Operator only", () => {
  assert.equal(showDeleteAction({ canDelete: true }, true), true);
  assert.equal(showDeleteAction({ canDelete: false }, true), false);
  // An older API without the field never shows the action.
  assert.equal(showDeleteAction({}, true), false);
  assert.equal(showDeleteAction({ canDelete: null }, true), false);
  // A Viewer never sees it, whatever the backend says.
  assert.equal(showDeleteAction({ canDelete: true }, false), false);
});

test("deleteErrorMessage maps each 409 code to a plain message", () => {
  assert.match(deleteErrorMessage(409, "document-filed"), /Send it back to review first\./);
  assert.match(deleteErrorMessage(409, "document-attached"), /attached to the invoice in Acumatica/);
  assert.match(deleteErrorMessage(409, "document-processing"), /Wait until processing ends/);
  assert.match(deleteErrorMessage(409, null), /can no longer be deleted/);
  assert.match(deleteErrorMessage(409, "something-new"), /can no longer be deleted/);
});

test("deleteErrorMessage explains the other answers", () => {
  assert.match(deleteErrorMessage(400), /at most 500 characters/);
  assert.match(deleteErrorMessage(404), /no longer exists/);
  assert.match(deleteErrorMessage(403), /Admin or Operator/);
  assert.match(deleteErrorMessage(500), /error 500\)\. Nothing was deleted/);
});

test("the delete notice and the unconfirmed delete message", () => {
  assert.equal(documentDeletedMessage, "Document deleted.");
  assert.equal(unconfirmedMessage("deleted"), "We could not confirm whether the document was deleted. Reload the page to see its state.");
});
