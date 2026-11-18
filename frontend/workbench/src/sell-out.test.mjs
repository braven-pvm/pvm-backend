import assert from "node:assert/strict";
import test from "node:test";
import { describeMonths, describeReceivedBy, describeSellOutRefusal } from "./sell-out.mjs";

test("each refusal code has plain words", () => {
  assert.match(describeSellOutRefusal("already-loaded:3f2a"), /loaded already/);
  assert.match(describeSellOutRefusal("unknown-format"), /does not know this file/);
  assert.match(describeSellOutRefusal("several-formats:wholesaler-cj,wholesaler-topmed"), /more than one wholesaler list/);
  assert.match(describeSellOutRefusal("too-many-bad-rows"), /5 of each 100 rows/);
  assert.equal(describeSellOutRefusal("bad-format:The report has no column 'Site'."), "The file does not have its usual shape: The report has no column 'Site'.");
  assert.equal(describeSellOutRefusal("something new"), "something new");
  assert.equal(describeSellOutRefusal(null), "The file was not loaded.");
});

test("months show as a range", () => {
  assert.equal(describeMonths(["2026-06-01", "2026-07-01", "2026-11-01"]), "Jun 2026 to Nov 2026");
  assert.equal(describeMonths(["2026-10-01"]), "Oct 2026");
  assert.equal(describeMonths([]), "—");
});

test("who sent the file", () => {
  assert.equal(describeReceivedBy("upload:person@example.com"), "person@example.com");
  assert.equal(describeReceivedBy("mail:person@example.com"), "person@example.com (mail)");
});
