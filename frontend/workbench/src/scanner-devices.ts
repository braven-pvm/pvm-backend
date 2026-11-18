/*
 * Pure helpers for the Scanner devices page: labels, the expiry text, name
 * checks and the messages for each action. The API shapes are in
 * src/api/scanner-devices.ts. Design: docs/superpowers/specs/2026-11-17-document-capture-design.md 6.10.
 */

export type ScannerDeviceState = "Pending" | "Active" | "Revoked";

/** The longest device or staff name the backend accepts. */
export const maxScannerNameLength = 100;

const stateLabels: Record<string, string> = {
  Pending: "Pending",
  Active: "Active",
  Revoked: "Revoked",
};

export function deviceStateLabel(state: string): string {
  return stateLabels[state] ?? state;
}

/** A new setup code is for a device that is not revoked. A revoked phone is added again as a new device. */
export function canIssueSetupCode(state: string): boolean {
  return state === "Pending" || state === "Active";
}

/** "Valid until 11:42, once", in South African time (UTC+2, no daylight saving). */
export function setupCodeExpiryText(expiresAt: string): string {
  const time = Date.parse(expiresAt);
  if (Number.isNaN(time)) {
    return "Valid for 15 minutes, once";
  }
  const shifted = new Date(time + 2 * 60 * 60 * 1000);
  const hours = String(shifted.getUTCHours()).padStart(2, "0");
  const minutes = String(shifted.getUTCMinutes()).padStart(2, "0");
  return `Valid until ${hours}:${minutes}, once`;
}

export type NameCheck = { ok: true; name: string } | { ok: false; error: string };

/** A device or staff name: trimmed, 1 to 100 characters, as the backend checks it. */
export function validateScannerName(value: unknown): NameCheck {
  const name = typeof value === "string" ? value.trim() : "";
  if (!name) {
    return { ok: false, error: "Enter a name." };
  }
  if (name.length > maxScannerNameLength) {
    return { ok: false, error: `Use at most ${maxScannerNameLength} characters. This name has ${name.length}.` };
  }
  return { ok: true, name };
}

export const notConfiguredMessage =
  "Scanner setup is not configured for this environment. Ask IT to set DocumentCapture:DeviceApiBaseUrl.";

/** The backend's 500 when DocumentCapture:DeviceApiBaseUrl is missing or not a valid https URL. */
export function isNotConfigured(status: number, detail: string | null | undefined): boolean {
  return status === 500 && typeof detail === "string" && detail.startsWith("Scanner setup is not configured");
}

const nameRule = `Enter a name of at most ${maxScannerNameLength} characters.`;
const deviceGone = "This device no longer exists. The page now shows the current list.";

export function addDeviceErrorMessage(status: number, name: string): string {
  switch (status) {
    case 409:
      return `A device named “${name}” already exists. Choose another name.`;
    case 400:
      return nameRule;
    case 403:
      return "Only an Admin can add scanner devices.";
    default:
      return `The device could not be added (error ${status}). Try again in a minute.`;
  }
}

export function setupCodeErrorMessage(status: number): string {
  switch (status) {
    case 404:
      return deviceGone;
    case 409:
      return "This device is revoked. Add the phone again as a new device.";
    case 403:
      return "Only an Admin can make setup codes.";
    default:
      return `The setup code could not be made (error ${status}). Try again in a minute.`;
  }
}

export function revokeErrorMessage(status: number): string {
  switch (status) {
    case 404:
      return deviceGone;
    case 403:
      return "Only an Admin can revoke scanner devices.";
    default:
      return `The device could not be revoked (error ${status}). It may still work. Try again in a minute.`;
  }
}

export function staffErrorMessage(status: number): string {
  switch (status) {
    case 400:
      return nameRule;
    case 404:
      return "This staff name no longer exists. The page now shows the current list.";
    case 403:
      return "Only an Admin can change staff names.";
    default:
      return `The staff name could not be saved (error ${status}). Nothing changed. Try again in a minute.`;
  }
}
