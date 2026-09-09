import assert from "node:assert/strict";
import { test } from "node:test";
import { describeNedbankImport, formatMoney } from "./formatters.mjs";

test("formatMoney renders missing money values as a dash", () => {
  assert.equal(formatMoney("ZAR", null), "-");
  assert.equal(formatMoney("ZAR", undefined), "-");
});

test("formatMoney renders present money values with two decimals", () => {
  assert.equal(formatMoney("ZAR", 12), "ZAR 12.00");
});

test("describeNedbankImport reports that nothing changed when no line is new", () => {
  assert.equal(
    describeNedbankImport({ fileName: "aug.ofx", linesImported: 0 }),
    "aug.ofx contained no new transactions. Acumatica was not changed.",
  );
});

test("describeNedbankImport reports the line count and the statement reference", () => {
  assert.equal(
    describeNedbankImport({ fileName: "aug.ofx", linesImported: 12, statementReference: "000123" }),
    "aug.ofx: imported 12 transactions as statement 000123.",
  );
});

test("describeNedbankImport uses the singular for one transaction", () => {
  assert.equal(
    describeNedbankImport({ fileName: "aug.ofx", linesImported: 1, statementReference: "000123" }),
    "aug.ofx: imported 1 transaction as statement 000123.",
  );
});

test("describeNedbankImport omits the reference when Acumatica returned none", () => {
  assert.equal(
    describeNedbankImport({ fileName: "aug.ofx", linesImported: 3 }),
    "aug.ofx: imported 3 transactions.",
  );
});
