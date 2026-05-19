# Workbench Authentication And Roles Implementation Plan

## Status

Planned.

The QA infrastructure is active and deployable through GitHub Actions OIDC. The current blocker before real Acumatica staging or Shoprite QA data is access control.

## Current State

- Workbench is public and unauthenticated.
- API invoice endpoints are public and unauthenticated.
- Workbench server actions call the API directly.
- API submission audit uses a hardcoded initiated-by value of `admin`.
- QA URLs are live:
  - API: `https://ca-pvm-api-qa.lemonocean-3257d28f.southafricanorth.azurecontainerapps.io`
  - Workbench: `https://ca-pvm-workbench-qa.lemonocean-3257d28f.southafricanorth.azurecontainerapps.io`

## Recommendation

Use Microsoft Entra ID for the first auth implementation.

Reasons:

- PVM already has Azure and Microsoft identity context.
- It avoids introducing a separate SaaS identity provider before the domain is stable.
- It fits internal staff/admin access better than customer-facing auth.
- It lets staff use their existing Microsoft work account and MFA.
- It should be used for identity only; PVM app-managed roles should decide what each user can do.
- This avoids making day-to-day role membership dependent on Azure/Entra administration.

Decision:

- Microsoft Entra ID proves who the user is.
- PVM stores application users, roles, status, and permissions in its own database.
- The admin console manages memberships directly.
- Azure/Entra groups are not the operational role source for MVP.

## Roles

| Role | Workbench access | API access |
| --- | --- | --- |
| `Admin` | Full access, config, future replay/dead-letter tools, role-sensitive operations | All invoice read/write endpoints, future config/replay endpoints |
| `Operator` | Invoice queue, candidate detail, refresh, revalidate, submit | Read invoice candidates, refresh/revalidate, submit |
| `Viewer` | Invoice queue, candidate detail, validation, XML preview, attempts | Read-only invoice candidate/attempt endpoints |

Initial role rules:

- All authenticated roles can view invoice queues and details.
- `Viewer` cannot refresh, revalidate, or submit.
- `Operator` can refresh, revalidate, and submit.
- `Admin` can do everything and will own future configuration/mapping/dead-letter controls.

User status rules:

- Only pre-authorized users can access the workbench after Microsoft sign-in.
- A signed-in Microsoft user that is not pre-authorized is denied access and is not shown the operational console.
- A pre-authorized user can be created by an app `Admin`, or by the bootstrap admin allowlist.
- Disabled users cannot access the workbench or API even if Microsoft sign-in succeeds.
- Role changes take effect on the next request/session refresh.

## App-Managed Authorization Model

Persist users and role assignments in PostgreSQL.

Suggested tables:

```text
app_users
  id
  entra_object_id
  email
  display_name
  status
  created_at
  updated_at
  last_login_at

app_user_roles
  id
  app_user_id
  role
  granted_by_app_user_id
  granted_at

app_user_audit_events
  id
  actor_app_user_id
  target_app_user_id
  action
  before_json
  after_json
  created_at
```

Role management lives in the workbench admin console:

- list users
- invite/pre-authorize users by email
- assign/remove roles
- disable/enable users
- inspect user audit events

Bootstrap:

- Configure one or more bootstrap admin emails/object IDs via Key Vault/Container Apps config.
- On first sign-in, if the identity matches the bootstrap admin config, create or update the user as `Admin`.
- After bootstrap, all normal role changes happen in the admin console.
- Users not created by bootstrap or an Admin are denied after Microsoft sign-in.

Initial bootstrap admin:

| Field | Value |
| --- | --- |
| Email / UPN | `developer@pvm.co.za` |
| Display name | `Marius Bloemhof` |
| Entra object ID | `35425387-d19a-4e63-97b5-2165cce0032b` |

## Architecture

### Workbench

Add Entra-backed authentication to the Next.js workbench:

- Protect all workbench routes except the sign-in route.
- Add signed-in user context in the header.
- Resolve the signed-in user against the PVM user table before rendering protected pages.
- Add role-aware UI controls:
  - hide or disable refresh for `Viewer`
  - hide or disable submit for `Viewer`
  - keep future admin areas behind `Admin`
- Add admin-only user management screens.
- Forward an API bearer token from workbench server-side fetches to the API.
- Keep local development ergonomic with explicit `AUTH_MODE=DevelopmentBypass`.
- Hard-block `DevelopmentBypass` unless the app is running in a development environment.

### API

Add JWT bearer authentication and role authorization:

- Keep `/health` anonymous.
- Require authenticated users for `/api/invoices/*`.
- Resolve the bearer token's Entra object ID/email to an active PVM app user.
- Policies:
  - `Invoices.Read`: `Admin`, `Operator`, `Viewer`
  - `Invoices.Write`: `Admin`, `Operator`
  - `Admin`: `Admin`
- Replace hardcoded `initiatedBy = "admin"` with the authenticated user identity.
- Return `401` for unauthenticated requests and `403` for insufficient role.

### Azure / Entra

Create separate Entra app registrations if needed:

- Workbench client app.
- API resource app with scopes/audience accepted by the API.

Do not manage PVM roles in Azure groups for MVP. Entra remains the login provider; PostgreSQL remains the role source.

Store auth config in Key Vault / Container Apps secrets:

```text
auth--tenantid
auth--clientid
auth--clientsecret
auth--issuer
auth--audience
auth--bootstrapadminemails
auth--bootstrapadminobjectids
```

Do not commit auth secrets.

## Implementation Slices

### Slice 1: Auth Design And Entra Setup

Deliverables:

- Confirm Entra tenant and user/group assignment model.
- Create app registrations.
- Add required callback/logout URLs for QA.
- Store required secrets in Key Vault.
- Document setup in a runbook.

Acceptance:

- A named admin user can sign into the QA workbench.
- Bootstrap admin is created or updated in the PVM app user table.

### Slice 2: Workbench Route Protection

Deliverables:

- Add auth package and configuration.
- Add sign-in/sign-out.
- Protect workbench pages.
- Show signed-in user and role in the header.
- Add role-aware UI controls.
- Add admin-only user management pages.

Acceptance:

- Anonymous users cannot access `/invoices`.
- Authenticated users can access allowed pages.
- Authenticated users that are not pre-authorized are denied and cannot access the console.
- Role restrictions are reflected in UI controls.

### Slice 3: API JWT And Role Policies

Deliverables:

- Add JWT bearer auth.
- Add role policies.
- Add app-user lookup and active-user requirement.
- Protect invoice endpoints.
- Preserve `/health` as anonymous.
- Update submission audit user from token claims.

Acceptance:

- Anonymous API invoice calls return `401`.
- Authenticated users without an active app role return `403`.
- `Viewer` write calls return `403`.
- `Operator` submit call is accepted if the candidate is otherwise valid.
- Attempt history records the authenticated user.

### Slice 4: Admin User Management

Deliverables:

- Admin user list.
- User detail page.
- Grant/remove `Admin`, `Operator`, `Viewer`.
- Disable/enable users.
- Audit user-management changes.

Acceptance:

- An Admin can add an Operator without entering Azure.
- An Admin can disable a user and their next request is denied.
- Role changes are audited.

### Slice 5: Workbench-To-API Token Flow

Deliverables:

- Workbench server-side API client obtains/forwards API access token.
- API base URL remains environment-configurable.
- Errors surface cleanly in server actions.

Acceptance:

- Workbench can load invoice candidates through the protected API.
- Refresh and submit work through the protected API for allowed roles.

### Slice 6: QA Deployment And Smoke

Deliverables:

- Update Container Apps secret/env config in Bicep.
- Update GitHub Actions deployment if new secrets or variables are needed.
- Add authenticated smoke-test notes.

Acceptance:

- CI/CD deploy passes.
- Anonymous workbench access is blocked.
- Admin/operator user can complete fixture refresh and submit workflow.
- Viewer user can view but cannot submit.

## Test Plan

Backend:

- Unit/endpoint tests for auth policies.
- Token/claims tests for app-user lookup.
- App-user repository tests for bootstrap, roles, disable, and audit.
- Submission audit test proves authenticated username is persisted.

Frontend:

- Build/lint.
- Route protection smoke.
- Role-aware render tests if test framework is added.

Deployment:

- GitHub Actions deploy.
- Manual browser verification against QA with at least one Admin/Operator user and one Viewer user.

## Risks And Guardrails

- Do not connect real invoice/customer data until auth is live in QA.
- Do not rely only on hidden buttons; API policies must enforce authorization.
- Do not make Azure group membership the day-to-day admin surface for PVM roles.
- Do not grant access to every tenant user by default; default should be denied unless pre-authorized.
- Keep local development bypass impossible in QA/prod.
- Fail startup if `AUTH_MODE=DevelopmentBypass` is set outside development.

## Open Decisions

1. Should we use Microsoft Entra ID as recommended?
2. Confirm app-managed roles in PostgreSQL rather than Entra groups.
3. Decision: bootstrap Admin is `developer@pvm.co.za` / `35425387-d19a-4e63-97b5-2165cce0032b`.
4. Decision: only pre-authorized users may access the console after Microsoft sign-in.
5. Decision: use development-only auth bypass for local development, with startup/runtime guards that prevent it in QA/prod.
