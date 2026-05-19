# Azure QA Infrastructure Provisioning Playbook

This playbook defines the first Azure QA environment for the PVM Acumatica/Shoprite integration platform.

The immediate goal is to deploy a safe QA shell for the fixture-backed invoice upload vertical slice, then progressively wire Acumatica staging and Shoprite QA. Do not add production credentials or send production traffic from this environment.

## Current Azure Context

Verified by Azure CLI on 2026-05-19:

| Item | Value |
| --- | --- |
| Tenant ID | `cf6de706-07fd-492e-9ff7-13234a0961a6` |
| Subscription name | `PVM-01` |
| Subscription ID | `51497af4-8223-42c4-a2ef-f6f625094d2f` |
| CLI user | `developer@pvm.co.za` |
| User object ID | `35425387-d19a-4e63-97b5-2165cce0032b` |
| Current role | `Owner` at subscription scope |
| Existing resource groups | none |
| Existing resources | none |
| Subscription policy assignments | none found |
| Preferred region | `southafricanorth` |

Known inherited role:

- `pieter-admin@pvm.co.za` has `User Access Administrator` at `/`.

## Provisioning Principles

- Keep all QA resources in one isolated resource group: `rg-pvm-integrations-qa`.
- Use infrastructure as code from the repo, not portal-only resource creation.
- Use Azure-native Bicep for the first baseline.
- Use deterministic names, short enough for Azure resource constraints.
- Use managed identity where possible.
- Store secrets in Key Vault or Container Apps secrets, never in Git.
- Put a budget alert in place before running expensive workloads.
- Default to QA cost containment over high availability.
- Do not wire real Shoprite or Acumatica production credentials.

## Target Resource Inventory

| Resource | Proposed name | Purpose | Initial SKU/stance |
| --- | --- | --- | --- |
| Resource group | `rg-pvm-integrations-qa` | Project isolation | South Africa North |
| Azure Container Registry | `acrpvmintegrationsqa` | Store API/workbench images | Standard, West Europe |
| Log Analytics workspace | `log-pvm-integrations-qa` | Container/app logs | Pay-as-you-go, capped |
| Application Insights | `appi-pvm-integrations-qa` | API/workbench telemetry | Workspace-based |
| Container Apps Environment | `cae-pvm-integrations-qa` | Host container apps | Consumption |
| API Container App | `ca-pvm-api-qa` | .NET API | min replicas 0 initially |
| Workbench Container App | `ca-pvm-workbench-qa` | Next.js admin UI | min replicas 0 initially |
| PostgreSQL Flexible Server | `psql-pvm-integrations-qa` | Operational state DB | Burstable B1ms/B2s class, small storage |
| PostgreSQL database | `pvm` | App database | single DB |
| Key Vault | `kv-pvm-int-qa` | Secrets | Standard |
| Storage Account | `stpvmintegrationsqa` | Payload archive | Standard LRS, hot |
| Blob container | `payloads` | XML/JSON payloads | private |
| Service Bus namespace | `sb-pvm-integrations-qa` | Future queue/dead-letter processing | Standard, can defer |
| Managed identity | `id-pvm-integrations-qa` | App access to Azure resources | user-assigned |
| Budget | `budget-pvm-integrations-qa` | Cost guardrail | alert at $100 and $150 |

Name availability must be checked before final deployment because ACR, Key Vault, Storage, and PostgreSQL names are globally constrained.

## Provisioned QA Baseline

Provisioning was run from branch `infra/azure-qa-baseline` on 2026-05-19.

| Item | Value |
| --- | --- |
| Resource group | `rg-pvm-integrations-qa` |
| API URL | `https://ca-pvm-api-qa.lemonocean-3257d28f.southafricanorth.azurecontainerapps.io` |
| Workbench URL | `https://ca-pvm-workbench-qa.lemonocean-3257d28f.southafricanorth.azurecontainerapps.io` |
| Image tag | `qa-20260519-01` and `qa-latest` |
| ACR login server | `acrpvmintegrationsqa.azurecr.io` |
| PostgreSQL FQDN | `psql-pvm-integrations-qa.postgres.database.azure.com` |
| Key Vault | `kv-pvm-int-qa` |
| Storage account | `stpvmintegrationsqa` |
| Service Bus namespace | `sb-pvm-integrations-qa` |
| Managed identity | `id-pvm-integrations-qa` |

Smoke results:

- API `/health` returned `{"status":"ok"}`.
- API `/api/invoices/candidates` returned fixture invoice `INV342699282`.
- Workbench `/invoices` returned HTTP 200 and rendered `INV342699282`.

Deployment notes:

- Azure Container Registry `Basic` and `Standard` failed in `southafricanorth` during Bicep deployment with `SkuNotSupported`; the registry is deployed in `westeurope` while runtime/data resources remain in `southafricanorth`.
- Container Apps revisions were created with Azure CLI after the Bicep platform deployment. The next CI/CD slice should codify app revision updates or replace the CLI step with a deploy workflow.
- Both app revisions use the user-assigned managed identity for ACR pull.
- The QA PostgreSQL firewall currently allows public access for early operator testing. Tighten this before staging/production data is connected.
- The workbench is public and unauthenticated. Do not connect real customer/invoice data until authentication and roles are in place.

## Cost Guardrails

Initial expected monthly cost:

- Lean QA, scale-to-zero where possible: roughly USD 45-75/month.
- Always-on QA with more logs: roughly USD 85-130/month.

Cost controls:

- Use Container Apps consumption with `minReplicas = 0` for workbench/API until active QA starts.
- Set Log Analytics daily cap or low-retention policy.
- Use PostgreSQL burstable, smallest acceptable storage, no HA.
- Defer Service Bus if the next slice does not need workers/queues.
- Add Azure budget alerts before broad testing.

Recommended budget:

- alert at USD 100 forecast/actual
- escalation at USD 150 forecast/actual

Partner billing may differ due to CSP pricing, exchange rate, VAT, support, or management margin.

## Provider Registration

Register required providers once per subscription:

```powershell
az account set --subscription 51497af4-8223-42c4-a2ef-f6f625094d2f

az provider register --namespace Microsoft.App
az provider register --namespace Microsoft.ContainerRegistry
az provider register --namespace Microsoft.DBforPostgreSQL
az provider register --namespace Microsoft.KeyVault
az provider register --namespace Microsoft.ManagedIdentity
az provider register --namespace Microsoft.OperationalInsights
az provider register --namespace Microsoft.ServiceBus
az provider register --namespace Microsoft.Storage
az provider register --namespace Microsoft.Insights
```

Check registration:

```powershell
az provider list `
  --query "[?namespace=='Microsoft.App' || namespace=='Microsoft.ContainerRegistry' || namespace=='Microsoft.DBforPostgreSQL' || namespace=='Microsoft.KeyVault' || namespace=='Microsoft.ManagedIdentity' || namespace=='Microsoft.OperationalInsights' || namespace=='Microsoft.ServiceBus' || namespace=='Microsoft.Storage' || namespace=='Microsoft.Insights'].{namespace:namespace,registrationState:registrationState}" `
  --output table
```

Proceed only when required providers are `Registered`.

## Repository Work Plan

Create a dedicated infra slice after this playbook:

```text
branch: infra/azure-qa-baseline
issue: PVM-18
```

Files to add:

```text
infra/
  azure/
    main.bicep
    main.parameters.qa.json
    modules/
      acr.bicep
      container-apps.bicep
      key-vault.bicep
      observability.bicep
      postgres.bicep
      service-bus.bicep
      storage.bicep
      budget.bicep
backend/
  src/Pvm.Api/Dockerfile
frontend/
  workbench/Dockerfile
.github/
  workflows/deploy-qa.yml
docs/runbooks/
  azure-qa-provisioning-playbook.md
```

For the first deployment, Bicep should create the platform resources. Container image build/push can be a separate step if GitHub OIDC is not ready yet.

## Bicep Deployment Shape

Use subscription-scope deployment so the resource group can be created by IaC:

```powershell
az deployment sub what-if `
  --location southafricanorth `
  --template-file infra/azure/main.bicep `
  --parameters infra/azure/main.parameters.qa.json
```

Apply:

```powershell
$postgresPassword = "<generated-secure-password>"

az deployment sub create `
  --location southafricanorth `
  --template-file infra/azure/main.bicep `
  --parameters infra/azure/main.parameters.qa.json postgresAdminPassword=$postgresPassword
```

Suggested parameter values:

```json
{
  "environmentName": {
    "value": "qa"
  },
  "location": {
    "value": "southafricanorth"
  },
  "resourceGroupName": {
    "value": "rg-pvm-integrations-qa"
  },
  "ownerObjectId": {
    "value": "35425387-d19a-4e63-97b5-2165cce0032b"
  },
  "monthlyBudgetAmountUsd": {
    "value": 100
  },
  "alertEmail": {
    "value": "developer@pvm.co.za"
  }
}
```

Pass `postgresAdminPassword` at deployment time; do not store it in `main.parameters.qa.json`.

Required tags:

```text
Project=PVM Integrations
Environment=QA
Owner=PVM
ManagedBy=Codex/IaC
CostCentre=PVM
DataClassification=Confidential
```

## Initial Networking Stance

QA baseline:

- Workbench ingress public but protected by future auth before real credentials are used.
- API ingress can be public temporarily for health checks, but prefer Container Apps internal ingress once workbench-to-API routing is settled.
- PostgreSQL public network access may be allowed only from Azure services/current operator IP for early QA; private networking should be revisited before production.
- Key Vault and Storage can start with public network restricted by RBAC/keys for QA; private endpoints are production-hardening work.

Production target:

- private API ingress
- private PostgreSQL
- private Key Vault/Storage endpoints
- workbench behind identity-aware access
- partner-approved firewall/allowlist model

## Secrets Plan

Do not put these values in Bicep parameter files:

- PostgreSQL admin password
- Shoprite username/password
- Acumatica username/password or OAuth secret
- GitHub/Azure deployment credentials

Initial secret names:

```text
postgres-admin-password
connectionstrings--pvm
shoprite--baseurl
shoprite--username
shoprite--password
shoprite--contractid
shoprite--uiuser
acumatica--baseurl
acumatica--tenant
acumatica--branch
acumatica--username
acumatica--password
acumatica--endpointname
acumatica--endpointversion
```

Set secrets after Key Vault exists:

```powershell
az keyvault secret set --vault-name kv-pvm-int-qa --name shoprite--baseurl --value "https://..."
```

For local operator testing, `.env` remains local only and must not be committed.

## Identity And RBAC

Use a user-assigned managed identity for the API/workbench:

```text
id-pvm-integrations-qa
```

Assign:

- Key Vault Secrets User on `kv-pvm-int-qa`
- Storage Blob Data Contributor on `stpvmintegrationsqa`
- AcrPull on `acrpvmintegrationsqa` for container apps identity if needed

If GitHub Actions deploys:

- create an Entra app/federated credential for the GitHub repo
- assign Contributor to `rg-pvm-integrations-qa`
- assign AcrPush to ACR
- avoid client secrets; prefer OIDC federation

## Docker Image Plan

API image:

- build from `backend/src/Pvm.Api`
- .NET 10 SDK build stage
- .NET 10 ASP.NET runtime final stage
- expose port 8080 or 5000 consistently
- run `/health` after deployment

Workbench image:

- build from `frontend/workbench`
- Node.js LTS image
- `npm ci`
- `npm run build`
- run `next start`
- set `NEXT_PUBLIC_API_BASE_URL` for the deployed API URL

Manual build/push commands once ACR exists:

```powershell
az acr login --name acrpvmintegrationsqa

docker build -t acrpvmintegrationsqa.azurecr.io/pvm-api:qa-latest -f backend/src/Pvm.Api/Dockerfile .
docker build -t acrpvmintegrationsqa.azurecr.io/pvm-workbench:qa-latest -f frontend/workbench/Dockerfile frontend/workbench

docker push acrpvmintegrationsqa.azurecr.io/pvm-api:qa-latest
docker push acrpvmintegrationsqa.azurecr.io/pvm-workbench:qa-latest
```

The initial QA deployment used:

```text
acrpvmintegrationsqa.azurecr.io/pvm-api:qa-20260519-01
acrpvmintegrationsqa.azurecr.io/pvm-workbench:qa-20260519-01
```

## Deployment Phases

### Phase 0: Confirm Azure Readiness

Commands:

```powershell
az account show
az group list
az policy assignment list --disable-scope-strict-match --scope /subscriptions/51497af4-8223-42c4-a2ef-f6f625094d2f
az provider list --query "[?registrationState!='Registered'].namespace" --output table
```

Expected:

- subscription is `PVM-01`
- no blocking policy assignments
- required providers registered

### Phase 1: IaC What-If

Run Bicep what-if and inspect:

```powershell
az deployment sub what-if `
  --location southafricanorth `
  --template-file infra/azure/main.bicep `
  --parameters infra/azure/main.parameters.qa.json
```

Proceed only if:

- all resources are in `rg-pvm-integrations-qa`
- no unexpected regions
- no premium/high-cost SKUs
- no production-like settings
- no secrets in parameter files

### Phase 2: Provision Platform

Apply deployment:

```powershell
$postgresPassword = "<generated-secure-password>"

az deployment sub create `
  --location southafricanorth `
  --template-file infra/azure/main.bicep `
  --parameters infra/azure/main.parameters.qa.json postgresAdminPassword=$postgresPassword
```

Verify:

```powershell
az resource list -g rg-pvm-integrations-qa --output table
az monitor log-analytics workspace list -g rg-pvm-integrations-qa --output table
az acr show -n acrpvmintegrationsqa --query "{name:name,loginServer:loginServer,sku:sku.name}" --output table
```

### Phase 3: Set Secrets

Minimum for fixture QA:

- PostgreSQL connection string
- generated database password if not already handled by Container Apps secret

Do not set Shoprite/Acumatica real values until connector switch is ready.

### Phase 4: Build And Push Images

Build and push API/workbench images to ACR.

Verify:

```powershell
az acr repository list -n acrpvmintegrationsqa --output table
az acr repository show-tags -n acrpvmintegrationsqa --repository pvm-api --output table
az acr repository show-tags -n acrpvmintegrationsqa --repository pvm-workbench --output table
```

### Phase 5: Deploy Container Apps

Deploy API and workbench revisions.

Initial API deployment shape:

```powershell
$resourceGroup = "rg-pvm-integrations-qa"
$environment = "cae-pvm-integrations-qa"
$identityId = az identity show -g $resourceGroup -n id-pvm-integrations-qa --query id -o tsv
$connectionString = az keyvault secret show --vault-name kv-pvm-int-qa --name connectionstrings--pvm --query value -o tsv
$loginServer = "acrpvmintegrationsqa.azurecr.io"

az containerapp create `
  --name ca-pvm-api-qa `
  --resource-group $resourceGroup `
  --environment $environment `
  --image "$loginServer/pvm-api:qa-latest" `
  --ingress external `
  --target-port 8080 `
  --user-assigned $identityId `
  --registry-server $loginServer `
  --registry-identity $identityId `
  --cpu 0.25 `
  --memory 0.5Gi `
  --min-replicas 0 `
  --max-replicas 2 `
  --secrets "connectionstrings-pvm=$connectionString" `
  --env-vars ASPNETCORE_ENVIRONMENT=Development ConnectionStrings__Pvm=secretref:connectionstrings-pvm
```

Initial workbench deployment shape:

```powershell
$apiBase = "https://ca-pvm-api-qa.lemonocean-3257d28f.southafricanorth.azurecontainerapps.io"

az containerapp create `
  --name ca-pvm-workbench-qa `
  --resource-group $resourceGroup `
  --environment $environment `
  --image "$loginServer/pvm-workbench:qa-latest" `
  --ingress external `
  --target-port 3000 `
  --user-assigned $identityId `
  --registry-server $loginServer `
  --registry-identity $identityId `
  --cpu 0.25 `
  --memory 0.5Gi `
  --min-replicas 0 `
  --max-replicas 2 `
  --env-vars NODE_ENV=production NEXT_PUBLIC_API_BASE_URL=$apiBase
```

Verify:

```powershell
az containerapp list -g rg-pvm-integrations-qa --output table
az containerapp show -g rg-pvm-integrations-qa -n ca-pvm-api-qa --query "properties.configuration.ingress.fqdn" --output tsv
az containerapp show -g rg-pvm-integrations-qa -n ca-pvm-workbench-qa --query "properties.configuration.ingress.fqdn" --output tsv
```

Smoke test:

```powershell
$api = "https://<api-fqdn>"
Invoke-RestMethod "$api/health"
Invoke-RestMethod -Method Post "$api/api/invoices/refresh"
Invoke-RestMethod "$api/api/invoices/candidates"
```

Open workbench:

```text
https://<workbench-fqdn>/invoices
```

### Phase 6: Cost And Log Check

Check costs:

```powershell
az consumption usage list --start-date <yyyy-mm-dd> --end-date <yyyy-mm-dd> --output table
```

Check logs:

```powershell
az containerapp logs show -g rg-pvm-integrations-qa -n ca-pvm-api-qa --follow
az containerapp logs show -g rg-pvm-integrations-qa -n ca-pvm-workbench-qa --follow
```

## Acceptance Criteria

Provisioning baseline is ready when:

- required resource providers are registered
- `rg-pvm-integrations-qa` exists
- ACR exists and accepts image pushes
- Log Analytics workspace exists
- Container Apps Environment exists
- API container app is deployed
- workbench container app is deployed
- PostgreSQL is reachable by the API
- Key Vault exists
- Storage Account and `payloads` container exist
- Service Bus exists or is explicitly deferred
- budget alerts exist
- API `/health` returns success
- workbench `/invoices` loads
- fixture refresh works in deployed QA
- generated XML can be viewed in deployed workbench

## Rollback And Teardown

For a bad app revision:

```powershell
az containerapp revision list -g rg-pvm-integrations-qa -n ca-pvm-api-qa --output table
az containerapp revision activate -g rg-pvm-integrations-qa -n ca-pvm-api-qa --revision <previous-revision>
```

For full QA teardown:

```powershell
az group delete --name rg-pvm-integrations-qa --yes
```

Before teardown:

- confirm no useful payload/archive data is needed
- export PostgreSQL if needed
- export Container Apps logs if needed
- notify the partner if billing reports are expected

## Follow-Up Issues

Suggested execution breakdown:

1. `PVM-19`: Register Azure providers and create Bicep baseline.
2. `PVM-20`: Add Dockerfiles and local image build checks.
3. `PVM-21`: Provision QA resource group and platform services.
4. `PVM-22`: Deploy API/workbench containers to QA.
5. `PVM-23`: Add GitHub Actions OIDC deployment.
6. `PVM-24`: Add real Acumatica staging connector configuration.
7. `PVM-25`: Switch API from local Shoprite stub to real Shoprite QA client behind config.

## Do-Not-Cross Lines

- Do not put secrets in Git.
- Do not send production Shoprite traffic.
- Do not connect production Acumatica.
- Do not leave unauthenticated workbench access in place once real customer/invoice data is used.
- Do not enable automatic submission before manual QA submission is accepted and duplicate/ambiguous handling is proven.
