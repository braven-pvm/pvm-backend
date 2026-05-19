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
- It supports app roles or groups for `Admin`, `Operator`, and `Viewer`.
- The same tenant can protect the workbench and issue tokens accepted by the API.

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

## Architecture

### Workbench

Add Entra-backed authentication to the Next.js workbench:

- Protect all workbench routes except the sign-in route.
- Add signed-in user context in the header.
- Add role-aware UI controls:
  - hide or disable refresh for `Viewer`
  - hide or disable submit for `Viewer`
  - keep future admin areas behind `Admin`
- Forward an API bearer token from workbench server-side fetches to the API.
- Keep local development ergonomic with either:
  - Entra dev app registration, or
  - explicit `AUTH_MODE=DevelopmentBypass` only outside QA/prod.

### API

Add JWT bearer authentication and role authorization:

- Keep `/health` anonymous.
- Require authenticated users for `/api/invoices/*`.
- Policies:
  - `Invoices.Read`: `Admin`, `Operator`, `Viewer`
  - `Invoices.Write`: `Admin`, `Operator`
  - `Admin`: `Admin`
- Replace hardcoded `initiatedBy = "admin"` with the authenticated user identity.
- Return `401` for unauthenticated requests and `403` for insufficient role.

### Azure / Entra

Create separate Entra app registrations if needed:

- Workbench client app.
- API resource app with scopes or app roles.

Recommended app roles:

```text
Pvm.Admin
Pvm.Operator
Pvm.Viewer
```

Store auth config in Key Vault / Container Apps secrets:

```text
auth--tenantid
auth--clientid
auth--clientsecret
auth--issuer
auth--audience
auth--allowedroles
```

Do not commit auth secrets.

## Implementation Slices

### Slice 1: Auth Design And Entra Setup

Deliverables:

- Confirm Entra tenant and user/group assignment model.
- Create app registrations.
- Define roles.
- Add required callback/logout URLs for QA.
- Store required secrets in Key Vault.
- Document setup in a runbook.

Acceptance:

- A named admin user can sign into the QA workbench.
- User has a visible role claim or group mapping path.

### Slice 2: Workbench Route Protection

Deliverables:

- Add auth package and configuration.
- Add sign-in/sign-out.
- Protect workbench pages.
- Show signed-in user and role in the header.
- Add role-aware UI controls.

Acceptance:

- Anonymous users cannot access `/invoices`.
- Authenticated users can access allowed pages.
- Role restrictions are reflected in UI controls.

### Slice 3: API JWT And Role Policies

Deliverables:

- Add JWT bearer auth.
- Add role policies.
- Protect invoice endpoints.
- Preserve `/health` as anonymous.
- Update submission audit user from token claims.

Acceptance:

- Anonymous API invoice calls return `401`.
- `Viewer` write calls return `403`.
- `Operator` submit call is accepted if the candidate is otherwise valid.
- Attempt history records the authenticated user.

### Slice 4: Workbench-To-API Token Flow

Deliverables:

- Workbench server-side API client obtains/forwards API access token.
- API base URL remains environment-configurable.
- Errors surface cleanly in server actions.

Acceptance:

- Workbench can load invoice candidates through the protected API.
- Refresh and submit work through the protected API for allowed roles.

### Slice 5: QA Deployment And Smoke

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
- Token/claims tests for role mapping.
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
- Avoid broad tenant-wide access unless explicitly approved.
- Prefer named user/group assignment over anyone-in-tenant access.
- Keep local development bypass impossible in QA/prod.

## Open Decisions

1. Should we use Microsoft Entra ID as recommended?
2. Should roles be assigned directly to users or through Entra groups?
3. Who are the initial Admin, Operator, and Viewer users?
4. Should QA require explicit app assignment, or allow any user in the tenant with no role as blocked/read-only?
5. Should local development use Entra sign-in or a development-only auth bypass?
