import assert from "node:assert/strict";
import { test } from "node:test";
import { formatMoney } from "./formatters.mjs";

test("formatMoney renders missing money values as a dash", () => {
  assert.equal(formatMoney("ZAR", null), "-");
  assert.equal(formatMoney("ZAR", undefined), "-");
});

test("formatMoney renders present money values with two decimals", () => {
  assert.equal(formatMoney("ZAR", 12), "ZAR 12.00");
});
