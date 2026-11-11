import { UserPlus } from "lucide-react";
import { listUsers, type AppUser } from "../../../src/api/admin";
import { hasAnyRole, requireWorkbenchUser } from "../../../src/auth/session";
import { formatDateTime } from "../../../src/formatters.mjs";
import { ButtonLink, DataTable, EmptyState, KeyValueList, ListPage, Notice, Panel, StatusBadge, type Column } from "../../../src/ui";

export const dynamic = "force-dynamic";
export const metadata = { title: "Users" };

const columns: Column<AppUser>[] = [
  { key: "name", header: "Name", render: (user) => user.displayName ?? user.email },
  { key: "email", header: "Email", render: (user) => user.email, weak: true },
  { key: "roles", header: "Roles", render: (user) => user.roles.join(", ") || "None" },
  { key: "login", header: "Last sign-in", render: (user) => (user.lastLoginAt ? formatDateTime(user.lastLoginAt) : "Never"), weak: true },
  { key: "status", header: "Status", render: (user) => <StatusBadge status={user.status} /> },
];

export default async function UsersPage() {
  const user = await requireWorkbenchUser("/admin/users");
  const header = { eyebrow: "Settings", title: "Users", description: "People who can sign in, and what each one may do." };

  if (!hasAnyRole(user, ["Admin"])) {
    return <ListPage header={header} notice={<Notice tone="info" title="Admin access needed">Only an Admin can see and change users.</Notice>} table={null} />;
  }

  const users = await listUsers();

  return (
    <ListPage
      header={{
        ...header,
        actions: (
          <ButtonLink href="/admin/users/new" variant="primary">
            <UserPlus aria-hidden="true" />
            Add user
          </ButtonLink>
        ),
      }}
      attention={
        <Panel title="Roles">
          <KeyValueList
            items={[
              { label: "Viewer", value: "Sees everything. Changes nothing." },
              { label: "Operator", value: "Imports statements, reads invoices and submits them to Shoprite." },
              { label: "Admin", value: "Everything, plus users, mappings, exceptions and automation policy." },
            ]}
          />
        </Panel>
      }
      table={
        <DataTable
          caption="Users"
          columns={columns}
          rows={users}
          rowKey={(row) => row.id}
          rowHref={(row) => `/admin/users/${row.id}`}
          empty={<EmptyState title="No users yet" action={<ButtonLink href="/admin/users/new">Add user</ButtonLink>} />}
        />
      }
    />
  );
}
