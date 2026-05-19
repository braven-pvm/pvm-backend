import {
  createUserAction,
  updateUserRolesAction,
  updateUserStatusAction,
} from "../../actions";
import { listUsers } from "../../../src/api/admin";
import { hasAnyRole, requireWorkbenchUser } from "../../../src/auth/session";

export const dynamic = "force-dynamic";

const roles = ["Admin", "Operator", "Viewer"];

export default async function UsersPage() {
  const user = await requireWorkbenchUser();
  if (!hasAnyRole(user, ["Admin"])) {
    return (
      <main className="page-shell">
        <section className="page-heading">
          <div>
            <h1>Users</h1>
            <p>You need Admin access to manage workbench users.</p>
          </div>
        </section>
      </main>
    );
  }

  const users = await listUsers();

  return (
    <main className="page-shell">
      <section className="page-heading">
        <div>
          <h1>User management</h1>
          <p>Pre-authorize Microsoft users and manage app roles.</p>
        </div>
      </section>

      <section className="table-panel detail-section">
        <div className="table-toolbar">
          <h2>Add user</h2>
          <span>pre-authorized only</span>
        </div>
        <form className="admin-form" action={createUserAction}>
          <label>
            Email
            <input name="email" type="email" required />
          </label>
          <label>
            Display name
            <input name="displayName" type="text" />
          </label>
          <fieldset>
            <legend>Roles</legend>
            {roles.map((role) => (
              <label key={role} className="check-row">
                <input name="roles" type="checkbox" value={role} />
                {role}
              </label>
            ))}
          </fieldset>
          <button className="button" type="submit">
            Add user
          </button>
        </form>
      </section>

      <section className="table-panel detail-section">
        <div className="table-toolbar">
          <h2>Authorized users</h2>
          <span>{users.length} records</span>
        </div>
        <table>
          <thead>
            <tr>
              <th>User</th>
              <th>Status</th>
              <th>Roles</th>
              <th>Last login</th>
              <th>Manage</th>
            </tr>
          </thead>
          <tbody>
            {users.map((managedUser) => (
              <tr key={managedUser.id}>
                <td data-label="User">
                  <strong>{managedUser.email}</strong>
                  <span>{managedUser.displayName ?? "-"}</span>
                </td>
                <td data-label="Status">{managedUser.status}</td>
                <td data-label="Roles">{managedUser.roles.join(", ")}</td>
                <td data-label="Last login">
                  {managedUser.lastLoginAt
                    ? new Date(managedUser.lastLoginAt).toLocaleString()
                    : "-"}
                </td>
                <td data-label="Manage">
                  <form className="inline-form" action={updateUserRolesAction}>
                    <input name="id" type="hidden" value={managedUser.id} />
                    {roles.map((role) => (
                      <label key={role} className="check-row">
                        <input
                          name="roles"
                          type="checkbox"
                          value={role}
                          defaultChecked={managedUser.roles.includes(role)}
                        />
                        {role}
                      </label>
                    ))}
                    <button className="button secondary" type="submit">
                      Save roles
                    </button>
                  </form>
                  <form className="inline-form" action={updateUserStatusAction}>
                    <input name="id" type="hidden" value={managedUser.id} />
                    <input
                      name="status"
                      type="hidden"
                      value={managedUser.status === "Active" ? "Disabled" : "Active"}
                    />
                    <button className="button secondary" type="submit">
                      {managedUser.status === "Active" ? "Disable" : "Enable"}
                    </button>
                  </form>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
    </main>
  );
}
