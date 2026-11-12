import assert from "node:assert/strict";
import { test } from "node:test";
import {
  formatDayAndDate,
  formatMoney,
  formatMoneyZar,
  formatStatementDate,
} from "./formatters.mjs";

test("formatMoney renders missing money values as a dash", () => {
  assert.equal(formatMoney("ZAR", null), "-");
  assert.equal(formatMoney("ZAR", undefined), "-");
});

test("formatMoney renders present money values with two decimals", () => {
  assert.equal(formatMoney("ZAR", 12), "ZAR 12.00");
});





test("formatMoneyZar groups thousands with a space and keeps two decimals", () => {
  assert.equal(formatMoneyZar(66465.58), "R 66 465.58");
  assert.equal(formatMoneyZar(1915.9), "R 1 915.90");
  assert.equal(formatMoneyZar(0), "R 0.00");
  assert.equal(formatMoneyZar(1234567.05), "R 1 234 567.05");
});

test("formatMoneyZar keeps the sign outside the currency symbol", () => {
  assert.equal(formatMoneyZar(-336.4), "-R 336.40");
});

test("formatMoneyZar renders a missing amount as a dash", () => {
  assert.equal(formatMoneyZar(null), "—");
  assert.equal(formatMoneyZar(undefined), "—");
});

test("formatStatementDate writes the month in full", () => {
  assert.equal(formatStatementDate("2026-09-14"), "14 September 2026");
  assert.equal(formatStatementDate("2026-01-01"), "1 January 2026");
});

test("formatStatementDate renders a missing date as a dash", () => {
  assert.equal(formatStatementDate(null), "—");
});

test("formatDayAndDate names the weekday, so a weekend gap explains itself", () => {
  assert.equal(formatDayAndDate("2026-09-13"), "Sunday 13 September");
  assert.equal(formatDayAndDate("2026-09-14"), "Monday 14 September");
});

test("formatDateTime shows South African time and leaves out the current year", async () => {
  const { formatDateTime } = await import("./formatters.mjs");
  const now = new Date("2026-11-11T08:00:00Z");
  assert.equal(formatDateTime("2026-11-11T03:00:00Z", now), "11 Nov, 05:00");
  assert.equal(formatDateTime("2025-12-31T23:30:00Z", now), "1 Jan, 01:30");
  assert.equal(formatDateTime("2025-06-01T10:15:00Z", now), "1 Jun 2025, 12:15");
  assert.equal(formatDateTime(null, now), "—");
});

test("formatDate shows the South African calendar day", async () => {
  const { formatDate } = await import("./formatters.mjs");
  assert.equal(formatDate("2026-11-10T23:30:00Z"), "11 Nov 2026");
  assert.equal(formatDate(undefined), "—");
});
