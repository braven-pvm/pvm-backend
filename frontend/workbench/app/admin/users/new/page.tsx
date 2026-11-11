import { notFound } from "next/navigation";
import { createUserAction } from "../../../actions";
import { hasAnyRole, requireWorkbenchUser } from "../../../../src/auth/session";
import { SettingsPage, SettingsSection, TextField } from "../../../../src/ui";
import { RolesField } from "../roles-field";

export const dynamic = "force-dynamic";
export const metadata = { title: "Add a user" };

export default async function NewUserPage() {
  const user = await requireWorkbenchUser("/admin/users/new");
  if (!hasAnyRole(user, ["Admin"])) {
    notFound();
  }

  return (
    <SettingsPage
      header={{
        eyebrow: "Settings · users",
        title: "Add a user",
        description: "The person signs in with their Microsoft work account. Add them here first, or sign-in is refused.",
      }}
    >
      <SettingsSection title="User" action={createUserAction} submitLabel="Add user" pendingLabel="Adding…">
        <TextField name="email" label="Work email" type="email" width="l" required />
        <TextField name="displayName" label="Name" width="l" />
        <RolesField />
      </SettingsSection>
    </SettingsPage>
  );
}
