import { getApiAuthHeaders } from "../auth/session";
import { timedFetch } from "./timed-fetch";

/*
 * The scanner device and staff name endpoints
 * (backend/src/Pvm.Api/Features/Documents/ScannerAdminEndpoints.cs). Admin only.
 * Server-side only: the calls carry the user's token. A setup code answer is
 * returned to the caller and never logged or stored; timedFetch logs only the
 * method, the path with ids masked, the status and the time.
 */

const apiBaseUrl = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5000";

export type ScannerDevice = {
  id: string;
  name: string;
  state: "Pending" | "Active" | "Revoked" | string;
  createdAt: string;
  enrolledAt: string | null;
  lastSeenAt: string | null;
  revokedAt: string | null;
};

/** A new setup code. qrPayload is the exact text the QR code must hold. */
export type IssuedSetupCode = {
  device: ScannerDevice;
  setupCode: string;
  expiresAt: string;
  qrPayload: string;
};

export type ScannerStaff = {
  id: string;
  name: string;
  active: boolean;
  createdAt: string;
  updatedAt: string;
};

/** A call whose failure the page explains. detail is the problem's detail, when the API sent one. */
export type ScannerResult<T> = { ok: true; value: T } | { ok: false; status: number; detail: string | null };

async function call<T>(method: "GET" | "POST" | "PUT", path: string, body?: unknown): Promise<ScannerResult<T>> {
  const headers = await getApiAuthHeaders();
  const response = await timedFetch(`${apiBaseUrl}${path}`, {
    method,
    headers: body === undefined ? headers : { ...headers, "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
    cache: "no-store",
  });
  if (!response.ok) {
    let detail: string | null = null;
    try {
      const problem = await response.json();
      detail = typeof problem?.detail === "string" ? problem.detail : null;
    } catch {
      detail = null;
    }
    return { ok: false, status: response.status, detail };
  }
  return { ok: true, value: (await response.json()) as T };
}

async function getList<T>(path: string, what: string): Promise<T[]> {
  const result = await call<T[]>("GET", path);
  if (!result.ok) {
    throw new Error(`Failed to load ${what}: ${result.status}`);
  }
  return result.value;
}

export function listScannerDevices(): Promise<ScannerDevice[]> {
  return getList("/api/scanner-devices", "scanner devices");
}

export function addScannerDevice(name: string): Promise<ScannerResult<IssuedSetupCode>> {
  return call("POST", "/api/scanner-devices", { name });
}

export function newSetupCode(id: string): Promise<ScannerResult<IssuedSetupCode>> {
  return call("POST", `/api/scanner-devices/${encodeURIComponent(id)}/setup-code`);
}

export function revokeScannerDevice(id: string): Promise<ScannerResult<ScannerDevice>> {
  return call("POST", `/api/scanner-devices/${encodeURIComponent(id)}/revoke`);
}

export function listScannerStaff(): Promise<ScannerStaff[]> {
  return getList("/api/scanner-staff", "staff names");
}

export function addScannerStaff(name: string): Promise<ScannerResult<ScannerStaff>> {
  return call("POST", "/api/scanner-staff", { name });
}

export function updateScannerStaff(id: string, name: string, active: boolean): Promise<ScannerResult<ScannerStaff>> {
  return call("PUT", `/api/scanner-staff/${encodeURIComponent(id)}`, { name, active });
}
