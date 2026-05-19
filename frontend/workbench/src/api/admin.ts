import { getApiAuthHeaders } from "../auth/session";

const apiBaseUrl =
  process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5000";

export type AppUser = {
  id: string;
  entraObjectId?: string;
  email: string;
  displayName?: string;
  status: string;
  roles: string[];
  createdAt: string;
  updatedAt: string;
  lastLoginAt?: string;
};

export async function listUsers(): Promise<AppUser[]> {
  const headers = await getApiAuthHeaders();
  const response = await fetch(`${apiBaseUrl}/api/admin/users`, {
    headers,
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(`Failed to load users: ${response.status}`);
  }

  return response.json();
}

export async function createUser(input: {
  email: string;
  displayName?: string;
  roles: string[];
}) {
  const headers = await getApiAuthHeaders();
  const response = await fetch(`${apiBaseUrl}/api/admin/users`, {
    method: "POST",
    headers: {
      ...headers,
      "Content-Type": "application/json",
    },
    body: JSON.stringify(input),
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(`Failed to create user: ${response.status}`);
  }
}

export async function updateUserRoles(id: string, roles: string[]) {
  const headers = await getApiAuthHeaders();
  const response = await fetch(`${apiBaseUrl}/api/admin/users/${id}/roles`, {
    method: "PUT",
    headers: {
      ...headers,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ roles }),
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(`Failed to update user roles: ${response.status}`);
  }
}

export async function updateUserStatus(id: string, status: string) {
  const headers = await getApiAuthHeaders();
  const response = await fetch(`${apiBaseUrl}/api/admin/users/${id}/status`, {
    method: "PUT",
    headers: {
      ...headers,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ status }),
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(`Failed to update user status: ${response.status}`);
  }
}
