import { getServerSession } from "next-auth";
import { redirect } from "next/navigation";
import { authOptions } from "./options";
import { isDevelopmentBypass } from "./config";

export type WorkbenchUser = {
  id: string;
  email: string;
  displayName?: string;
  roles: string[];
  accessToken?: string;
};

export async function getApiAuthHeaders(): Promise<Record<string, string>> {
  if (isDevelopmentBypass()) {
    return {
      "X-PVM-Dev-User-Email": "developer@pvm.co.za",
      "X-PVM-Dev-User-ObjectId": "35425387-d19a-4e63-97b5-2165cce0032b",
      "X-PVM-Dev-User-Name": "Marius Bloemhof",
    };
  }

  const session = await getServerSession(authOptions);
  if (!session?.accessToken) {
    console.warn("auth.headers.missing-access-token", {
      hasSession: Boolean(session),
      email: session?.user?.email,
    });
    return {};
  }

  return {
    Authorization: `Bearer ${session.accessToken}`,
  };
}

export async function requireWorkbenchUser(callbackPath = "/invoices"): Promise<WorkbenchUser> {
  const headers = await getApiAuthHeaders();
  if (Object.keys(headers).length === 0) {
    redirect(signInPath(callbackPath));
  }

  const apiBaseUrl = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5000";
  const response = await fetch(`${apiBaseUrl}/api/auth/me`, {
    headers,
    cache: "no-store",
  });
  console.info("auth.me.response", { status: response.status });

  if (response.status === 401) {
    redirect(signInPath(callbackPath));
  }

  if (response.status === 403) {
    redirect("/access-denied");
  }

  if (!response.ok) {
    throw new Error(`Failed to load current user: ${response.status}`);
  }

  const user = (await response.json()) as WorkbenchUser;
  return {
    ...user,
    accessToken: headers.Authorization,
  };
}

export function hasAnyRole(user: WorkbenchUser, roles: string[]) {
  return roles.some((role) => user.roles.includes(role));
}

function signInPath(callbackPath: string) {
  return `/sign-in?callbackUrl=${encodeURIComponent(normalizeCallbackPath(callbackPath))}`;
}

function normalizeCallbackPath(callbackPath: string) {
  return callbackPath.startsWith("/") && !callbackPath.startsWith("//")
    ? callbackPath
    : "/invoices";
}
