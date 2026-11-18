import assert from "node:assert/strict";
import { test } from "node:test";
import {
  addDeviceErrorMessage,
  canIssueSetupCode,
  deviceStateLabel,
  isNotConfigured,
  notConfiguredMessage,
  revokeErrorMessage,
  setupCodeErrorMessage,
  setupCodeExpiryText,
  staffErrorMessage,
  validateScannerName,
} from "./scanner-devices.ts";

test("each device state has a plain label", () => {
  assert.equal(deviceStateLabel("Pending"), "Pending");
  assert.equal(deviceStateLabel("Active"), "Active");
  assert.equal(deviceStateLabel("Revoked"), "Revoked");
});

test("an unknown device state is shown as it came", () => {
  assert.equal(deviceStateLabel("Lost"), "Lost");
});

test("a setup code can be made for a pending or active device, never a revoked one", () => {
  assert.equal(canIssueSetupCode("Pending"), true);
  assert.equal(canIssueSetupCode("Active"), true);
  assert.equal(canIssueSetupCode("Revoked"), false);
});

test("the expiry text gives the South African time and says the code works once", () => {
  // 09:42 UTC is 11:42 in South Africa.
  assert.equal(setupCodeExpiryText("2026-11-18T09:42:10.123+00:00"), "Valid until 11:42, once");
  assert.equal(setupCodeExpiryText("2026-11-18T23:05:00Z"), "Valid until 01:05, once");
});

test("an expiry the browser cannot read still says the code works once", () => {
  assert.equal(setupCodeExpiryText("not a time"), "Valid for 15 minutes, once");
});

test("a name is trimmed and must have 1 to 100 characters", () => {
  assert.deepEqual(validateScannerName("  Dispatch phone 1 "), { ok: true, name: "Dispatch phone 1" });
  assert.deepEqual(validateScannerName("x".repeat(100)), { ok: true, name: "x".repeat(100) });
  assert.deepEqual(validateScannerName("   "), { ok: false, error: "Enter a name." });
  assert.deepEqual(validateScannerName(null), { ok: false, error: "Enter a name." });
  assert.deepEqual(validateScannerName("x".repeat(101)), { ok: false, error: "Use at most 100 characters. This name has 101." });
});

test("the not-configured answer is recognised by its status and detail", () => {
  const detail = "Scanner setup is not configured: set DocumentCapture:DeviceApiBaseUrl";
  assert.equal(isNotConfigured(500, detail), true);
  assert.equal(isNotConfigured(500, "Something else broke"), false);
  assert.equal(isNotConfigured(500, null), false);
  assert.equal(isNotConfigured(409, detail), false);
  assert.equal(
    notConfiguredMessage,
    "Scanner setup is not configured for this environment. Ask IT to set DocumentCapture:DeviceApiBaseUrl.",
  );
});

test("add-device errors say what happened and what to do", () => {
  assert.equal(addDeviceErrorMessage(409, "Dispatch phone 1"), "A device named “Dispatch phone 1” already exists. Choose another name.");
  assert.equal(addDeviceErrorMessage(400, "x"), "Enter a name of at most 100 characters.");
  assert.equal(addDeviceErrorMessage(403, "x"), "Only an Admin can add scanner devices.");
  assert.equal(addDeviceErrorMessage(502, "x"), "The device could not be added (error 502). Try again in a minute.");
});

test("setup-code errors say what happened and what to do", () => {
  assert.equal(setupCodeErrorMessage(404), "This device no longer exists. The page now shows the current list.");
  assert.equal(setupCodeErrorMessage(409), "This device is revoked. Add the phone again as a new device.");
  assert.equal(setupCodeErrorMessage(503), "The setup code could not be made (error 503). Try again in a minute.");
});

test("revoke and staff errors say what happened", () => {
  assert.equal(revokeErrorMessage(404), "This device no longer exists. The page now shows the current list.");
  assert.equal(revokeErrorMessage(500), "The device could not be revoked (error 500). It may still work. Try again in a minute.");
  assert.equal(staffErrorMessage(400), "Enter a name of at most 100 characters.");
  assert.equal(staffErrorMessage(404), "This staff name no longer exists. The page now shows the current list.");
  assert.equal(staffErrorMessage(500), "The staff name could not be saved (error 500). Nothing changed. Try again in a minute.");
});
