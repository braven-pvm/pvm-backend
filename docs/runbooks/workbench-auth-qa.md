# Workbench Authentication QA Runbook

This runbook records the QA authentication setup for the PVM workbench and API.

## Model

- Microsoft Entra ID handles sign-in.
- The PVM API validates Entra access tokens.
- PVM stores application users and roles in PostgreSQL.
- Access is deny-by-default: a signed-in Microsoft user must be pre-authorized before reaching the workbench.
- Local development may use `AUTH_MODE=DevelopmentBypass`; this is guarded so it cannot start outside a development environment.

## Roles

| Role | Access |
| --- | --- |
| `Admin` | Full access, including user management. |
| `Operator` | View invoices and run refresh/revalidate/submit actions. |
| `Viewer` | Read-only invoice and status visibility. |

`Admin` users cannot disable their own account or remove their own `Admin` role through the admin console. The API also prevents removing or disabling the last active admin.

## Entra Applications

| App | Value |
| --- | --- |
| API display name | `pvm-backend-qa-api` |
| API client ID | `2ea883c8-83df-4cd5-9beb-9421552713ce` |
| API identifier URI | `api://2ea883c8-83df-4cd5-9beb-9421552713ce` |
| API scope | `api://2ea883c8-83df-4cd5-9beb-9421552713ce/access_as_user` |
| Workbench display name | `pvm-backend-qa-workbench` |
| Workbench client ID | `b0ad7b66-5063-48c2-b524-0a673b127ad3` |
| Workbench callback URL | `https://ca-pvm-workbench-qa.lemonocean-3257d28f.southafricanorth.azurecontainerapps.io/api/auth/callback/azure-ad` |

## Key Vault Secrets

Required secrets in `kv-pvm-int-qa`:

```text
auth--tenantid
auth--api-clientid
auth--workbench-clientid
auth--workbench-clientsecret
auth--apiscope
auth--nextauthsecret
auth--bootstrapadminemails
auth--bootstrapadminobjectids
```

Do not write secret values into Git, tickets, logs, or PR descriptions.

## Bootstrap Admin

QA bootstrap admin:

| Item | Value |
| --- | --- |
| Email | `developer@pvm.co.za` |
| Entra object ID | `35425387-d19a-4e63-97b5-2165cce0032b` |

On first successful sign-in by the bootstrap admin, the API creates the app user and grants `Admin`.

## Smoke Checks

After deployment:

1. `GET /health` returns `200`.
2. Anonymous `GET /api/invoices/candidates` returns `401`.
3. Workbench `/invoices` redirects into the Microsoft sign-in flow when no session exists.
4. Bootstrap admin can sign in and access `/admin/users`.
5. A non-pre-authorized Microsoft user signs in but lands on access denied.

## User Management

Admins manage workbench access in the workbench itself:

- Open `/admin/users`.
- Pre-authorize a user by email and role.
- The user's Entra object ID is captured on first sign-in when possible.
- Disable a user instead of deleting them so audit history remains stable.
