import { cache } from "react";
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

type CurrentUserResult =
  | { status: "signed-out" }
  | { status: "forbidden" }
  | { status: "error"; httpStatus: number }
  | { status: "ok"; user: WorkbenchUser };

/** One /api/auth/me call per request, shared by the layout and the page. */
const loadCurrentUser = cache(async (): Promise<CurrentUserResult> => {
  const headers = await getApiAuthHeaders();
  if (Object.keys(headers).length === 0) {
    return { status: "signed-out" };
  }

  const apiBaseUrl = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5000";
  let response: Response;
  try {
    response = await fetch(`${apiBaseUrl}/api/auth/me`, {
      headers,
      cache: "no-store",
    });
  } catch (error) {
    console.warn("auth.me.unreachable", { error: String(error) });
    return { status: "error", httpStatus: 0 };
  }
  console.info("auth.me.response", { status: response.status });

  if (response.status === 401) {
    return { status: "signed-out" };
  }

  if (response.status === 403) {
    return { status: "forbidden" };
  }

  if (!response.ok) {
    return { status: "error", httpStatus: response.status };
  }

  const user = (await response.json()) as WorkbenchUser;
  return { status: "ok", user: { ...user, accessToken: headers.Authorization } };
});

/** The signed-in user, or null. Never redirects; for the layout and navigation. */
export async function getCurrentUser(): Promise<WorkbenchUser | null> {
  const result = await loadCurrentUser();
  return result.status === "ok" ? result.user : null;
}

export async function requireWorkbenchUser(callbackPath = "/invoices"): Promise<WorkbenchUser> {
  const result = await loadCurrentUser();

  if (result.status === "signed-out") {
    redirect(signInPath(callbackPath));
  }

  if (result.status === "forbidden") {
    redirect("/access-denied");
  }

  if (result.status === "error") {
    throw new Error(`Failed to load current user: ${result.httpStatus}`);
  }

  return result.user;
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
