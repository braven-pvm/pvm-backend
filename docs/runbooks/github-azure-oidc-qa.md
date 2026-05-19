# GitHub Azure OIDC QA Deployment

This runbook records the passwordless GitHub Actions deployment identity for the PVM QA environment.

## GitHub Environment

Repository:

```text
braven-pvm/pvm-backend
```

Environment:

```text
qa
```

Environment variables:

| Variable | Value |
| --- | --- |
| `AZURE_CLIENT_ID` | `5e2f54fb-4db7-4e2c-841c-7dfa81e505af` |
| `AZURE_TENANT_ID` | `cf6de706-07fd-492e-9ff7-13234a0961a6` |
| `AZURE_SUBSCRIPTION_ID` | `51497af4-8223-42c4-a2ef-f6f625094d2f` |

No GitHub client secret is required. The workflow uses GitHub OIDC with `id-token: write`.

## Azure Identity

App registration / service principal:

| Item | Value |
| --- | --- |
| Display name | `pvm-backend-qa-deploy` |
| Application client ID | `5e2f54fb-4db7-4e2c-841c-7dfa81e505af` |
| App object ID | `2d8e6367-8b53-4ee3-b15a-c00e23bdf7fc` |
| Service principal object ID | `95ac18a9-23f8-434d-a113-f0988c5af90f` |

Federated credentials:

| Name | Subject |
| --- | --- |
| `pvm-backend-qa-main` | `repo:braven-pvm/pvm-backend:ref:refs/heads/main` |
| `pvm-backend-qa-environment` | `repo:braven-pvm/pvm-backend:environment:qa` |

## Azure Roles

The deployment identity currently has:

| Role | Scope | Reason |
| --- | --- | --- |
| `Contributor` | `/subscriptions/51497af4-8223-42c4-a2ef-f6f625094d2f` | Subscription-scope Bicep deployment, resource group, budget, and resource updates. |
| `Owner` | `/subscriptions/51497af4-8223-42c4-a2ef-f6f625094d2f/resourceGroups/rg-pvm-integrations-qa` | Allows Bicep-managed role assignments inside the QA resource group. |
| `AcrPush` | `acrpvmintegrationsqa` | Push API and workbench images. |
| `Key Vault Secrets User` | `kv-pvm-int-qa` | Read the existing PostgreSQL connection string during deployment. |

The role footprint can be tightened later by moving the budget/RG creation out of the deploy workflow and replacing broad subscription `Contributor` with resource-group scoped roles.

## Workflow

Workflow file:

```text
.github/workflows/deploy-qa.yml
```

The workflow:

1. Runs backend tests.
2. Runs workbench lint/build.
3. Logs into Azure via OIDC.
4. Reads the existing PostgreSQL connection string and workbench auth values from Key Vault.
5. Builds and pushes API/workbench images to ACR.
6. Runs the Bicep deployment with the pushed image tag and Entra auth configuration.
7. Smoke-tests API health, verifies anonymous invoice API access returns `401`, and verifies the workbench auth flow is reachable.

## Operational Guardrails

- Do not add Azure client secrets to GitHub; keep OIDC only.
- Do not connect real Shoprite or Acumatica data unless the QA workbench remains protected by Microsoft Entra authentication and app-managed roles.
- The workflow currently uses the existing QA PostgreSQL admin password from Key Vault to avoid rotating the password on every deployment.
- If the Key Vault connection string is intentionally rotated, the workflow automatically uses the new value on the next run.
- Required auth secrets must exist in `kv-pvm-int-qa` before deployment:
  - `auth--tenantid`
  - `auth--api-clientid`
  - `auth--workbench-clientid`
  - `auth--workbench-clientsecret`
  - `auth--apiscope`
  - `auth--nextauthsecret`
  - `auth--bootstrapadminemails`
  - `auth--bootstrapadminobjectids`
