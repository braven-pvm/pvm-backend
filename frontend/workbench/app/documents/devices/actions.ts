"use server";

import QRCode from "qrcode";
import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import {
  addScannerDevice,
  addScannerStaff,
  newSetupCode,
  revokeScannerDevice,
  updateScannerStaff,
  type IssuedSetupCode,
  type ScannerResult,
} from "../../../src/api/scanner-devices";
import { hasAnyRole, requireWorkbenchUser } from "../../../src/auth/session";
import { isDocumentId } from "../../../src/documents";
import {
  addDeviceErrorMessage,
  isNotConfigured,
  revokeErrorMessage,
  setupCodeErrorMessage,
  setupCodeExpiryText,
  staffErrorMessage,
  validateScannerName,
} from "../../../src/scanner-devices";

const pagePath = "/documents/devices";

/**
 * What the setup code panel shows. A new code lives only here: the action
 * returns it to the client component, which holds it in memory. It is never
 * put in the URL, a cookie, browser storage or a log, so a reload shows no code.
 * Only the QR code (SVG) goes to the browser, not the code as text.
 */
export type SetupCodeState =
  | { kind: "idle" }
  | { kind: "code"; key: string; deviceId: string; deviceName: string; qrSvg: string; expiresAt: string; expiryText: string }
  | { kind: "notConfigured" }
  | { kind: "error"; message: string; addName?: string };

async function requireAdmin() {
  const user = await requireWorkbenchUser(pagePath);
  if (!hasAnyRole(user, ["Admin"])) {
    throw new Error("Only an Admin can manage scanner devices.");
  }
}

async function toCodeState(issued: IssuedSetupCode): Promise<SetupCodeState> {
  // The QR holds the backend's qrPayload exactly as it came. Made here, on the server; no outside service sees it.
  const qrSvg = await QRCode.toString(issued.qrPayload, { type: "svg", errorCorrectionLevel: "M", margin: 4 });
  return {
    kind: "code",
    key: `${issued.device.id}:${issued.expiresAt}`,
    deviceId: issued.device.id,
    deviceName: issued.device.name,
    qrSvg,
    expiresAt: issued.expiresAt,
    expiryText: setupCodeExpiryText(issued.expiresAt),
  };
}

/** Add device (intent "add") or New setup code (intent "renew"). Both answer with a setup code. */
export async function issueSetupCodeAction(_previous: SetupCodeState, formData: FormData): Promise<SetupCodeState> {
  await requireAdmin();
  const intent = formData.get("intent");

  let result: ScannerResult<IssuedSetupCode>;
  if (intent === "add") {
    const rawName = formData.get("name");
    const addName = typeof rawName === "string" ? rawName : "";
    const check = validateScannerName(rawName);
    if (!check.ok) {
      return { kind: "error", message: check.error, addName };
    }
    try {
      result = await addScannerDevice(check.name);
    } catch {
      revalidatePath(pagePath);
      return { kind: "error", message: "We could not confirm whether the device was added. Check the list; if it is there, make a new setup code for it.", addName };
    }
    if (!result.ok) {
      if (isNotConfigured(result.status, result.detail)) {
        return { kind: "notConfigured" };
      }
      return { kind: "error", message: addDeviceErrorMessage(result.status, check.name), addName };
    }
  } else {
    const deviceId = formData.get("deviceId");
    if (!isDocumentId(deviceId)) {
      return { kind: "error", message: "The device id is missing. Reload the page and try again." };
    }
    try {
      result = await newSetupCode(deviceId);
    } catch {
      return { kind: "error", message: "We could not confirm whether a new setup code was made. Make a new setup code again." };
    }
    if (!result.ok) {
      if (isNotConfigured(result.status, result.detail)) {
        return { kind: "notConfigured" };
      }
      revalidatePath(pagePath);
      return { kind: "error", message: setupCodeErrorMessage(result.status) };
    }
  }

  revalidatePath(pagePath);
  return toCodeState(result.value);
}

/** Back to the page with the result. Only names and messages go in the URL, never a code. */
function finish(result: { status?: string; error?: string }): never {
  revalidatePath(pagePath);
  const query = result.error
    ? `error=${encodeURIComponent(result.error)}`
    : `status=${encodeURIComponent(result.status ?? "Done.")}`;
  redirect(`${pagePath}?${query}`);
}

function text(formData: FormData, name: string): string {
  const value = formData.get(name);
  return typeof value === "string" ? value.trim() : "";
}

export async function revokeDeviceAction(formData: FormData) {
  await requireAdmin();
  const deviceId = formData.get("deviceId");
  const name = text(formData, "deviceName") || "The device";
  if (!isDocumentId(deviceId)) {
    finish({ error: "The device id is missing. Reload the page and try again." });
  }

  let result: Awaited<ReturnType<typeof revokeScannerDevice>>;
  try {
    result = await revokeScannerDevice(deviceId);
  } catch {
    finish({ error: `We could not confirm whether ${name} was revoked. Reload the page to see its state.` });
  }
  if (!result.ok) {
    finish({ error: revokeErrorMessage(result.status) });
  }
  finish({ status: `${name} is revoked. It can no longer upload.` });
}

export async function addStaffAction(formData: FormData) {
  await requireAdmin();
  const check = validateScannerName(formData.get("name"));
  if (!check.ok) {
    finish({ error: check.error });
  }

  let result: Awaited<ReturnType<typeof addScannerStaff>>;
  try {
    result = await addScannerStaff(check.name);
  } catch {
    finish({ error: "We could not confirm whether the staff name was added. Reload the page to see the list." });
  }
  if (!result.ok) {
    finish({ error: staffErrorMessage(result.status) });
  }
  finish({ status: `${check.name} is added. The phones show it at the next batch.` });
}

/** Rename, or turn a staff name off or on. The API takes the name and the state together. */
export async function updateStaffAction(formData: FormData) {
  await requireAdmin();
  const staffId = formData.get("staffId");
  const check = validateScannerName(formData.get("name"));
  const active = formData.get("active") === "true";
  if (!isDocumentId(staffId)) {
    finish({ error: "The staff name id is missing. Reload the page and try again." });
  }
  if (!check.ok) {
    finish({ error: check.error });
  }

  let result: Awaited<ReturnType<typeof updateScannerStaff>>;
  try {
    result = await updateScannerStaff(staffId, check.name, active);
  } catch {
    finish({ error: "We could not confirm whether the staff name was saved. Reload the page to see the list." });
  }
  if (!result.ok) {
    finish({ error: staffErrorMessage(result.status) });
  }
  const previous = text(formData, "previousName");
  const change =
    formData.get("change") === "state"
      ? active
        ? `${check.name} is on. The phones show it at the next batch.`
        : `${check.name} is off. The phones no longer show it.`
      : `${previous || "The staff name"} is renamed to ${check.name}.`;
  finish({ status: change });
}
