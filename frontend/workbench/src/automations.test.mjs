import assert from "node:assert/strict";
import { test } from "node:test";
import { findAutomation, healthFromImportedThrough, healthFromRuns, modeFromPolicy, runsFor } from "./automations.ts";

const now = new Date("2026-11-12T08:00:00Z");
const run = (trigger, status, minutesAgo) => ({
  runType: "acumatica-invoice-reconciliation",
  trigger,
  status,
  createdAt: new Date(now.getTime() - minutesAgo * 60_000).toISOString(),
  completedAt: new Date(now.getTime() - minutesAgo * 60_000 + 30_000).toISOString(),
});

test("invoice reading and the lookback split the same run type by trigger", () => {
  const runs = [run("scheduled", "Succeeded", 5), run("daily-lookback", "Succeeded", 300), run("manual", "Succeeded", 50)];
  assert.equal(runsFor(findAutomation("invoice-reading"), runs).length, 2);
  assert.equal(runsFor(findAutomation("invoice-lookback"), runs).length, 1);
});

test("health is failing when the latest finished run failed", () => {
  const runs = [run("scheduled", "Failed", 5), run("scheduled", "Succeeded", 15)];
  assert.equal(healthFromRuns(findAutomation("invoice-reading"), runs, now), "Failing");
});

test("a running run does not hide the last result", () => {
  const runs = [run("scheduled", "Running", 1), run("scheduled", "Succeeded", 8)];
  assert.equal(healthFromRuns(findAutomation("invoice-reading"), runs, now), "Healthy");
});

test("health is late after two intervals without a success", () => {
  assert.equal(healthFromRuns(findAutomation("invoice-reading"), [run("scheduled", "Succeeded", 25)], now), "Late");
  assert.equal(healthFromRuns(findAutomation("invoice-reading"), [run("scheduled", "Succeeded", 15)], now), "Healthy");
});

test("no runs means unknown, and off or planned automations are off", () => {
  assert.equal(healthFromRuns(findAutomation("invoice-reading"), [], now), "Unknown");
  assert.equal(healthFromRuns(findAutomation("shoprite-po-refresh"), [], now), "Off");
  assert.equal(healthFromRuns(findAutomation("stock-sync"), [], now), "Off");
});

test("the Investec feed is late only after three days", () => {
  assert.equal(healthFromImportedThrough("2026-11-10", now), "Healthy");
  assert.equal(healthFromImportedThrough("2026-11-07", now), "Late");
  assert.equal(healthFromImportedThrough(null, now), "Unknown");
});

test("policy modes map to Off, Shadow and Live", () => {
  assert.equal(modeFromPolicy("Disabled"), "Off");
  assert.equal(modeFromPolicy("Shadow"), "Shadow");
  assert.equal(modeFromPolicy("Allowlisted"), "Live");
  assert.equal(modeFromPolicy("Enabled"), "Live");
});
