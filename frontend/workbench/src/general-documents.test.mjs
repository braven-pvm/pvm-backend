import assert from "node:assert/strict";
import { test } from "node:test";
import {
  buildGeneralQuery,
  generalTitle,
  isGeneralDocument,
  noteLine,
  phoneAndStaff,
  untitledScan,
} from "./general-documents.ts";

test("the title is the typed one, else Claude's, else Untitled scan", () => {
  assert.equal(generalTitle({ title: "Fleet licence", autoTitle: "Vehicle licence disc" }), "Fleet licence");
  assert.equal(generalTitle({ title: "  ", autoTitle: " Vehicle licence disc " }), "Vehicle licence disc");
  assert.equal(generalTitle({ title: null, autoTitle: null }), untitledScan);
  assert.equal(generalTitle({}), "Untitled scan");
});

test("a general document is the GENERAL type, in any case", () => {
  assert.equal(isGeneralDocument("GENERAL"), true);
  assert.equal(isGeneralDocument("general"), true);
  assert.equal(isGeneralDocument("POD"), false);
  assert.equal(isGeneralDocument(undefined), false);
});

test("the note is one line, cut with an ellipsis", () => {
  assert.equal(noteLine("Keep for\n\nSARS  audit"), "Keep for SARS audit");
  assert.equal(noteLine(null), null);
  assert.equal(noteLine("   "), null);
  const long = noteLine("word ".repeat(40), 30);
  assert.equal(long.length, 30);
  assert.ok(long.endsWith("…"));
});

test("phone and staff read as one line", () => {
  assert.equal(phoneAndStaff({ scannerDeviceName: "Dispatch phone 1", scannerStaffName: "Thabo", uploadedBy: "device:Dispatch phone 1" }), "Dispatch phone 1 · Thabo");
  assert.equal(phoneAndStaff({ scannerDeviceName: "Dispatch phone 1", scannerStaffName: null, uploadedBy: "x" }), "Dispatch phone 1");
  assert.equal(phoneAndStaff({ uploadedBy: "izelle@example.test" }), "izelle@example.test");
});

test("the query for the API turns days into South African day bounds", () => {
  assert.equal(
    buildGeneralQuery({ q: " eskom ", device: "d1", staff: "", from: "2026-11-01", to: "2026-11-17", page: 2 }),
    "q=eskom&device=d1&from=2026-11-01T00%3A00%3A00%2B02%3A00&to=2026-11-18T00%3A00%3A00%2B02%3A00&page=2",
  );
});

test("the query for the browser keeps the days as typed and leaves out page 1 and bad days", () => {
  assert.equal(buildGeneralQuery({ q: "eskom", from: "2026-02-31", to: "2026-11-17", page: 1 }, { forApi: false }), "q=eskom&to=2026-11-17");
  assert.equal(buildGeneralQuery({}), "");
});
