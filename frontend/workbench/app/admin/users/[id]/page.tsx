import { notFound } from "next/navigation";
import { updateUserRolesAction, updateUserStatusAction } from "../../../actions";
import { listUsers } from "../../../../src/api/admin";
import { hasAnyRole, requireWorkbenchUser } from "../../../../src/auth/session";
import { formatDateTime } from "../../../../src/formatters.mjs";
import { ConfirmDialog, KeyValueList, RecordPage, SettingsSection, StatusBadge } from "../../../../src/ui";
import { RolesField } from "../roles-field";

export const dynamic = "force-dynamic";
export const metadata = { title: "User" };

export default async function UserPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const user = await requireWorkbenchUser(`/admin/users/${id}`);
  if (!hasAnyRole(user, ["Admin"])) {
    notFound();
  }

  const managed = (await listUsers()).find((item) => item.id === id);
  if (!managed) {
    notFound();
  }
  const active = managed.status === "Active";
  const name = managed.displayName ?? managed.email;

  return (
    <RecordPage
      back={{ href: "/admin/users", label: "Users" }}
      header={{
        eyebrow: "User",
        title: name,
        description: managed.email,
        actions: (
          <>
            <StatusBadge status={managed.status} />
            <ConfirmDialog
              triggerLabel={active ? "Disable user" : "Enable user"}
              triggerVariant={active ? "tertiary" : "primary"}
              title={active ? `Disable ${name}?` : `Enable ${name}?`}
              confirmLabel={active ? "Disable user" : "Enable user"}
              pendingLabel="Saving…"
              tone={active ? "danger" : "default"}
              action={updateUserStatusAction}
              fields={{ id: managed.id, status: active ? "Disabled" : "Active" }}
            >
              <p>
                {active
                  ? "The user cannot sign in until an Admin enables them again. Their history stays."
                  : "The user can sign in again with the roles below."}
              </p>
            </ConfirmDialog>
          </>
        ),
      }}
      summary={
        <KeyValueList
          items={[
            { label: "Roles", value: managed.roles.join(", ") || "None" },
            { label: "Last sign-in", value: managed.lastLoginAt ? formatDateTime(managed.lastLoginAt) : "Never" },
            { label: "Added", value: formatDateTime(managed.createdAt) },
            { label: "Changed", value: formatDateTime(managed.updatedAt) },
            { label: "Entra object ID", value: managed.entraObjectId, mono: true },
          ]}
        />
      }
    >
      <SettingsSection title="Roles" action={updateUserRolesAction} submitLabel="Save roles" pendingLabel="Saving…">
        <input type="hidden" name="id" value={managed.id} />
        <RolesField selected={managed.roles} />
      </SettingsSection>
    </RecordPage>
  );
}
