# Azure Provider Meeting Prep

This document prepares the infrastructure discussion with the Azure provider for the PVM Acumatica/Shoprite integration platform.

The preferred direction is **Azure managed services** rather than a single VM. The project is not a simple website: it is a long-running integration, sync, invoice-submission, audit, and future reporting platform. The architecture must support idempotency, retries, dead letters, payload evidence, manual operator actions, and later event/incremental reporting.

Pricing in this document uses Azure public retail USD pricing checked on 2026-05-21 via the Azure Retail Prices API and Azure pricing pages. Partner CSP pricing, VAT, exchange rate, management margin, support fees, reservations, and discounts may change the actual bill.

## Current Known Azure Context

| Item | Value |
| --- | --- |
| Tenant ID | `cf6de706-07fd-492e-9ff7-13234a0961a6` |
| Subscription name | `PVM-01` |
| Subscription ID | `51497af4-8223-42c4-a2ef-f6f625094d2f` |
| Preferred data/runtime region | `southafricanorth` |
| Current QA resource group | `rg-pvm-integrations-qa` |
| Current QA API | `ca-pvm-api-qa` |
| Current QA workbench | `ca-pvm-workbench-qa` |
| Current QA database | `psql-pvm-integrations-qa.postgres.database.azure.com` |
| Current QA Key Vault | `kv-pvm-int-qa` |
| Current QA Storage Account | `stpvmintegrationsqa` |
| Current QA Service Bus | `sb-pvm-integrations-qa` |
| Current QA Container Registry | `acrpvmintegrationsqa.azurecr.io` |

Note: Azure Container Registry was provisioned in `westeurope` because the required ACR SKU was not available in `southafricanorth` during the original QA deployment. ACR stores application container images, not business payloads. Runtime, database, queue, secrets, and payload storage should remain in `southafricanorth` where available.

## Recommendation

Use a managed Azure baseline:

- Azure Container Apps for the API, admin workbench, and background workers.
- PostgreSQL Flexible Server for operational state, user access, audit, idempotency, invoice status, and mapping tables.
- Azure Service Bus for durable background work, retries, and dead letters.
- Azure Blob Storage for immutable raw payload snapshots and API evidence.
- Azure Key Vault for secrets.
- Managed Identity for Azure resource access.
- Log Analytics and Application Insights for observability.
- Infrastructure-as-code for repeatable QA/staging/production environments.

Avoid a single VM except as a temporary emergency fallback. A VM can be made to work, but it moves operational responsibility onto us: patching, database backup, queue durability, app supervision, security hardening, secrets, monitoring, and recovery.

## Infrastructure Options

| Option | Summary | Best use | Approx monthly cost | Main trade-off |
| --- | --- | --- | --- | --- |
| Lean managed QA | Container Apps scale-to-zero, small PostgreSQL, Service Bus Standard, small logs/storage | Current QA/staging validation | Roughly USD 45-75 | Low cost, but cold starts and no production-grade networking |
| Active managed QA/staging | Container Apps min replicas where needed, more logs, Service Bus active, stronger monitoring | Daily testing and user acceptance | Roughly USD 85-130 | Better responsiveness, slightly higher monthly baseline |
| Production managed baseline | Separate production resource group, min replicas, stronger database tier, private networking, backups, alerting | Live invoice submission and future reporting | Roughly USD 150-400+ before final sizing | More services and configuration, but significantly lower operational risk |
| Pure VM | One Linux/Windows VM running Docker/app services, possibly local DB/queue | Short-lived prototype only | Roughly USD 70-100+ for a basic always-on VM once disk/monitoring/backup are included | Fewer Azure line items, but much higher maintenance and recovery burden |

## Component Table: Purpose, Cost, VM Comparison, Benefits

| Component | Recommended Azure service | Technical purpose | Layman purpose | Indicative Azure cost | VM comparison | Benefit over VM |
| --- | --- | --- | --- | --- | --- | --- |
| Resource isolation | Resource Group | Deployment boundary, RBAC scope, cost tags, lifecycle boundary, environment separation | A separate Azure folder and billing bucket for this project | No direct resource cost | VM can also be placed in a resource group | Keeps QA/staging/prod costs and access cleanly separated |
| Runtime hosting | Azure Container Apps, Consumption for QA; min replicas for production | Runs `.NET` API, admin workbench, and worker services as containers; supports revisions, scaling, ingress, env vars, and managed identity | Runs the app without managing a server | Standard requests about USD 0.40 per 1M; active usage about USD 0.000024 per vCPU-second and USD 0.000003 per GiB-second; QA can scale to zero | VM is always-on compute, for example B2s Linux about USD 0.0542/hour, about USD 39.57/month before disk and extras | No OS patching, cleaner deploy revisions, scale-to-zero for QA, easier separation between API/workbench/workers |
| Container image storage | Azure Container Registry Standard | Private Docker image registry for API, workbench, and workers | Secure storage for packaged app releases | Standard registry about USD 0.6666/day, about USD 20/month, plus about USD 0.10/GB-month | VM could pull from GitHub packages or store local images | Keeps image distribution private, supports CI/CD, works with managed identity pull |
| Operational database | Azure Database for PostgreSQL Flexible Server | Stores invoice state, PO references, users, roles, idempotency keys, mapping tables, audit records, sync checkpoints, and future reporting metadata | The system's structured ledger and filing cabinet | B1MS compute about USD 0.0215/hour, about USD 15.70/month; storage about USD 0.151/GB-month; backups extra | Self-host PostgreSQL on VM avoids a DB line item but makes backups, patching, restore, disk sizing, monitoring, and upgrades our responsibility | Managed backups, easier restore, better isolation from app failures, less operational risk |
| Durable work queue | Azure Service Bus Standard | Queues invoice submission jobs, retry jobs, refresh jobs, and dead-lettered failures; decouples UI/API from workers | A reliable to-do queue that does not lose failed work | Standard base about USD 10/month plus operation charges | VM would need RabbitMQ, Hangfire-only polling, or another local queue | Durable retries/dead letters, future event-driven reporting path, avoids lost work during restarts |
| Payload archive | Azure Blob Storage Hot LRS | Stores raw XML/JSON payloads, Shoprite responses, generated invoice documents, attachments, replay evidence, and audit snapshots | Safe archive of the exact documents sent and received | Hot LRS storage about USD 0.02/GB-month; writes about USD 0.06 per 10K; reads are low cost | VM local disk can store files, but must be backed up and protected manually | Cheap durable archive, independent of app/database, easier retention and evidence handling |
| Secret storage | Azure Key Vault Standard | Stores Shoprite credentials, Acumatica credentials, connection strings, API secrets, certificates, and future signing material | Secure safe for passwords and keys | About USD 0.03 per 10K operations | VM secrets end up in env files, disk, or manual secret stores | Better access control, auditability, managed identity access, no secrets in code or deployment logs |
| Identity | User-assigned Managed Identity | Allows Container Apps to access Key Vault, Storage, ACR, and other Azure resources without static passwords | App identity badge inside Azure | No meaningful direct cost | VM can use managed identity too, but container/service boundaries are less clean | Reduces credential sprawl and simplifies rotation |
| Observability | Log Analytics + Application Insights | Central logs, traces, metrics, request timings, exceptions, alerts, and operational diagnosis | Black-box recorder and dashboard for support | Monitor logs vary by ingestion; examples include platform logs around USD 0.365/GB and basic logs around USD 0.73/GB depending category | VM needs log agents and custom collection; local logs are easy to lose | Faster troubleshooting, audit trail, alerts, cross-service traceability |
| Cost guardrails | Azure Budget + alerts | Forecast/actual spend notifications and threshold alerts | Early warning if costs spike | Budget alerts have no material compute cost; notifications may have small charges depending channel | VM costs are predictable but hidden costs still appear through disks, backup, bandwidth, monitoring | Prevents silent cost growth as testing and reporting grow |
| CI/CD identity | GitHub OIDC + Azure Federated Credentials | Secretless deployment from GitHub Actions into Azure | Deployment without shared passwords | No direct Azure runtime cost | VM deploy usually uses SSH keys or manual scripts | Safer automated deploys, repeatable releases, less credential handling |
| Infrastructure as code | Bicep or Terraform | Defines resources, settings, tags, identities, role assignments, and environment parity | Written recipe for rebuilding the environment | No direct Azure runtime cost | VM can also be provisioned by IaC, but app/database/queue internals still need scripts | Repeatable QA/prod setup, reviewable changes, easier provider handover |
| Admin console hosting | Same Container Apps workbench | Runs operator/admin UI for invoice candidate selection, manual submission, payload review, user management, retries, and dead-letter operations | The staff/admin screen for managing the integration | Included in Container Apps usage; QA scale-to-zero possible | VM can host it, but deployment and isolation are weaker | Same managed runtime, auth integration, auditability, revision deployment |
| Future reporting warehouse | Start with PostgreSQL; later Azure SQL/Synapse/Fabric/Power BI path if needed | Stores reporting snapshots, incremental extracts, and derived reporting models | Reporting database for staff/sales/management views | Not required for MVP; future cost depends on reporting scale | VM-hosted reports can work initially but will not scale cleanly | Keeps path open for incremental reporting without redesigning the invoice platform |

## Why A Pure VM Is Not The Right Default

A VM is attractive because the first bill looks simple: one compute line item, one disk line item, and maybe one public IP. For a basic B2s Linux VM, public Azure retail pricing is about USD 39.57/month for compute. A P10 LRS disk is about USD 24/month. Once backup, monitoring, security, public IP, storage growth, and operational time are included, the real low-end VM cost is closer to USD 70-100/month before we count the cost of maintenance.

The bigger issue is not the bill. The bigger issue is responsibility.

With a VM, we become responsible for:

- OS patching.
- Docker runtime patching.
- Database installation, upgrades, tuning, backup, and restore.
- Queue installation, upgrades, and dead-letter handling.
- Disk growth and disk failure planning.
- Log retention and diagnostics.
- Secret storage and rotation.
- Service restart supervision.
- Disaster recovery.
- Security hardening.
- High availability design.

That is not aligned with the system we are building. The integration must be reliable enough to submit invoices, preserve evidence, prevent duplicates, and recover from partial failures. Managed services solve more of that operational surface directly.

## Cost Comparison: Managed Baseline vs VM

| Area | Managed Azure baseline | Pure VM baseline |
| --- | --- | --- |
| Low-usage QA cost | Roughly USD 45-75/month with scale-to-zero | Roughly USD 70-100/month for an always-on small VM once disk/backup/monitoring are included |
| Production cost | Roughly USD 150-400+ depending sizing, private networking, database tier, and log volume | Starts lower for one VM, but production-ready HA quickly needs multiple VMs, load balancing, external backup, managed DB or serious self-hosting work |
| App hosting | Container Apps revisions, autoscale, managed ingress | Manual Docker/systemd/supervisor setup |
| Database | Managed PostgreSQL with backup/restore path | Self-hosted DB unless adding managed DB separately |
| Queue/retries | Azure Service Bus native queues and dead letters | Self-host RabbitMQ/Redis/Hangfire polling or add external queue anyway |
| Payload evidence | Blob Storage durable archive | Local disk or separate storage setup |
| Secrets | Key Vault and managed identity | Env files, local secrets, or manual vault setup |
| Monitoring | Application Insights and Log Analytics | VM agent plus custom log shipping |
| Security patching | Mostly Azure-managed for platform pieces | Our responsibility |
| Failure blast radius | App, DB, queue, storage are separated | Single VM failure can take down everything |
| Scaling | Per app/worker scaling | Scale whole VM or redesign |
| Operational fit | Better for long-running integration and reporting platform | Acceptable only for short prototype |

## Provider Comparison

Azure is not the only provider that can host this architecture. AWS and Google Cloud can cover the full service set. Oracle Cloud Infrastructure can cover most or all of it, but is less attractive for this project unless pricing or existing provider support strongly favors it. Smaller platforms can host parts of the workload, but usually leave gaps around durable queues, private networking, identity, enterprise access control, observability, or managed database maturity.

The important comparison is not "can it run containers?" The important comparison is whether the provider can supply the full operating model: container runtime, managed relational database, durable queue/dead-lettering, object storage, secrets, identity/RBAC, logs/traces, CI/CD integration, budget controls, and production networking.

Cost assumptions for this comparison:

- One small API/admin app surface.
- One background worker path.
- One small managed PostgreSQL database.
- Durable queue/dead-letter capability.
- Object storage for payload evidence.
- Secrets store.
- Basic logs/metrics.
- Low QA/staging traffic, not production load.
- No high availability, no large reporting warehouse, no heavy egress.
- USD list pricing or public pricing, not partner/CSP/contract pricing.

| Provider | Equivalent services | Can cover full architecture? | Rough lean QA/staging monthly cost | Rough active/production-ready monthly cost | Cost notes | Benefits | Gaps / risks vs Azure for this project | VM comparison |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Azure | Container Apps, Azure Database for PostgreSQL Flexible Server, Service Bus, Blob Storage, Key Vault, Managed Identity, Application Insights, Log Analytics, ACR, Budgets | Yes, preferred | USD 45-75 | USD 150-400+ before final production sizing | Azure currently looks cost-effective for QA because Container Apps can scale to zero; PostgreSQL B1MS is about USD 15.70/month compute, ACR is about USD 20/month, Service Bus Standard is about USD 10/month, and storage/secrets/logs are small at QA volume | Best fit because we already use Azure/Microsoft identity, current QA is provisioned, Entra sign-in is aligned with staff accounts, and the provider relationship already exists | Some regional SKU limitations, for example ACR in South Africa North; private networking adds complexity; Container Apps scale-to-zero causes cold starts | Better than VM for reliability, queue durability, secrets, audit, managed database, and deployment revisions |
| AWS | ECS Fargate or App Runner, RDS PostgreSQL, SQS/SNS or EventBridge, S3, Secrets Manager/SSM, IAM, CloudWatch/X-Ray, ECR, Budgets | Yes | USD 65-140 | USD 200-500+ before final production sizing | AWS can be cheap at low volume, but the bill often grows through always-on container tasks/App Runner instances, RDS, CloudWatch logs, ALB/NAT if used, Secrets Manager per-secret charges, and data transfer. SQS/S3 themselves are usually cheap. | Very mature managed services, excellent queueing/event ecosystem, strong IAM, strong regional/global platform, broad partner familiarity | We would lose the direct Microsoft/Entra operational alignment unless we bridge identity; networking can become expensive/complex with NAT/ALB; provider currently seems Azure-oriented | Better than a VM for the same reasons as Azure, but less aligned with our Microsoft tenant and current QA work |
| Google Cloud | Cloud Run, Cloud SQL for PostgreSQL, Pub/Sub or Cloud Tasks, Cloud Storage, Secret Manager, IAM, Cloud Logging/Monitoring/Trace, Artifact Registry, Budgets | Yes | USD 45-110 | USD 150-450+ before final production sizing | Google Cloud Run is highly cost-effective for request-driven workloads and can scale to zero like Azure Container Apps. The main baseline cost is Cloud SQL. Logging can become a meaningful cost if left noisy. | Strong serverless container platform, good managed Postgres, good logging/monitoring, simple container deploy model | Less aligned with current Microsoft/Azure provider setup; Pub/Sub semantics differ from Service Bus queues/dead letters; identity integration with Microsoft users would need extra design | Better than a VM operationally, but migration from current Azure path would add unnecessary platform work |
| Oracle Cloud Infrastructure | Container Instances/OKE, Autonomous Database or PostgreSQL, Queue, Object Storage, Vault, IAM, Logging/Monitoring, Container Registry, Budgets | Mostly yes | USD 35-100 | USD 120-350+ before final production sizing | OCI can be price-competitive for compute/storage and may undercut the other hyperscalers in some configurations. The actual database choice matters a lot: Autonomous Database, PostgreSQL, or self-managed DB have very different cost and operations profiles. | Potentially attractive pricing; strong database heritage; broad IaaS/PaaS coverage | Less natural fit for .NET/Microsoft/Entra workflow; smaller local ecosystem for this project; provider support and operational familiarity need confirmation | Better than a VM if using OCI managed services, but not as aligned as Azure/AWS/GCP |
| DigitalOcean | App Platform/Droplets/Kubernetes, Managed PostgreSQL, Spaces, Container Registry, monitoring, basic secrets | Partial | USD 50-120 | USD 120-300+ if trying to approximate production controls | Predictable pricing for simple apps, but once we add managed Postgres, always-on workers, object storage, backups, and external queueing, the gap to hyperscalers narrows. | Simple developer experience and straightforward billing | No first-class equivalent to Azure Service Bus for durable enterprise queue/dead-letter workflow; weaker enterprise IAM/private networking/observability story for this use case | Better than a self-managed VM for simple hosting, but not enough for the full integration platform without extra services |
| Render / Fly.io / Railway | App hosting, managed Postgres, object storage integrations, environment secrets, logs | Partial | USD 40-150 | USD 150-350+ if forced into always-on services, larger DB, external queue, and log retention | These can be cheap for prototypes, but production costs become less predictable once the workload needs always-on workers, managed database, object storage, reliable queues, retention, and audit evidence. | Very fast to deploy prototypes and internal tools | Not a full enterprise cloud control plane; queueing, dead letters, private networking, RBAC, audit, compliance, budget controls, and provider-managed operations are weaker or require add-ons | Good prototype alternative to a VM, but not a strong production fit for invoice submission and audit workflows |
| Single VM on any provider | Linux/Windows VM with Docker Compose, PostgreSQL/RabbitMQ/local files or attached managed services | Technically yes, operationally weak | USD 70-100+ for a realistic small always-on VM baseline | USD 150-400+ once made production-ish with backup, monitoring, larger disk, redundancy, and operational controls | A bare VM may start around USD 40/month compute, but disk, backup, monitoring, public IP, security, and support move the real cost up. The hidden cost is admin time and failure recovery. | Simple mental model; fewer cloud services to learn | Becomes our responsibility to patch, secure, back up, monitor, recover, scale, and operate everything | Only acceptable for a disposable prototype, not the recommended path |

Conclusion: on cost alone, Azure and Google Cloud are likely the closest for a lean serverless/container QA environment because both can scale the app tier toward zero. AWS is fully capable but can become more expensive once always-on containers, load balancing, NAT, and CloudWatch are included. OCI may be cheaper in some configurations, but it carries adoption/support risk for this specific Microsoft-heavy environment. A single VM is not meaningfully cheaper once operated responsibly, and it is materially worse for reliability.

## QA/Staging Shape

For the current phase:

- Keep one QA/staging resource group: `rg-pvm-integrations-qa`.
- Keep runtime/data in `southafricanorth` where supported.
- Keep ACR in `westeurope` only if South Africa North SKU availability remains blocked.
- Use Container Apps Consumption with `minReplicas = 0` for low-cost QA unless slow cold starts become a blocker.
- Use PostgreSQL Flexible Server Burstable B1MS/B2S class while volume is low.
- Use Service Bus Standard now or soon, because invoice submission should be queue-backed and dead-letter capable.
- Store generated Shoprite XML, request payloads, responses, and validation errors in Blob Storage.
- Keep secrets in Key Vault.
- Keep budget alerts active at roughly USD 100 and USD 150 for QA.

Known QA compromise:

- Container Apps scale-to-zero can cause first-load delays. That is acceptable for early QA. For active testing, set `minReplicas = 1` on the workbench/API and accept a higher monthly cost.

## Production Shape

Production should be separate:

- `rg-pvm-integrations-prod`
- separate Container Apps environment
- separate PostgreSQL server/database
- separate Key Vault
- separate Storage Account
- separate Service Bus namespace
- separate App Insights/Log Analytics or clearly separated workspace/tags
- stricter network controls
- stronger backup and retention policy
- budget alerts and operational alerts

Production should not share QA secrets, queues, storage containers, or databases.

Production should likely use:

- `minReplicas = 1` or more for API/workbench to avoid cold starts.
- a dedicated worker Container App for invoice submission.
- Service Bus queues for invoice submission, retries, and dead letters.
- private PostgreSQL access.
- private endpoints or restricted networking for Key Vault/Storage/PostgreSQL if the provider requires it.
- operational alerts for failed submissions, queue depth, dead-letter count, API errors, and database availability.

## Questions For The Azure Provider

1. What are the exact CSP prices for these services under our agreement?
   - Azure Container Apps
   - Azure Database for PostgreSQL Flexible Server
   - Azure Service Bus Standard
   - Azure Blob Storage Hot LRS
   - Azure Key Vault Standard
   - Azure Container Registry Standard
   - Log Analytics and Application Insights
   - Managed disks and VMs for comparison

2. Can we keep all runtime/data resources in `southafricanorth`?

3. Is `westeurope` acceptable for ACR if the local region does not support the required ACR SKU?
   - ACR contains application images only.
   - Business payloads and databases remain in South Africa North.

4. What access should PVM have?
   - We need at least Contributor on the project resource groups.
   - Owner may be needed if we are managing role assignments directly.
   - Confirm whether PVM can create/manage managed identities, role assignments, Key Vault policies/RBAC, and federated credentials.

5. What policy constraints exist?
   - Allowed regions.
   - Public ingress rules.
   - Public PostgreSQL rules.
   - Private endpoint requirements.
   - Required Defender for Cloud settings.
   - Required Log Analytics retention.

6. What is the preferred production networking model?
   - Public workbench behind Microsoft Entra auth.
   - Private API ingress.
   - Private PostgreSQL.
   - Private Key Vault/Storage.
   - VNet integration for Container Apps.

7. What backup and retention should we apply?
   - PostgreSQL retention.
   - Blob retention/versioning.
   - Log retention.
   - Disaster recovery expectations.

8. What billing separation do they need?
   - Dedicated resource groups.
   - Tags: `Project=PVM Integrations`, `Environment=QA/Prod`, `Owner=PVM`, `CostCentre=PVM`, `ManagedBy=IaC`.
   - Budget alert recipients.

9. Do they support us deploying via GitHub Actions OIDC?
   - This avoids static Azure secrets in CI/CD.
   - It requires an Entra app registration or federated credential path.

10. Do they want to manage any components themselves?
    - Database backups.
    - Monitoring/alerts.
    - Security policy.
    - Network/private endpoint setup.
    - Budget alerts.

## Meeting Position

The short version to present:

> We want a separate Azure resource group for the PVM integration platform, with PVM able to deploy and manage the app services through infrastructure-as-code. We prefer Azure managed services over a single VM because this system must be reliable, auditable, retryable, and eventually reporting/event driven. A VM may look cheaper, but it pushes patching, backups, queues, monitoring, secrets, and recovery onto us. Managed services give us cleaner operations and lower long-term risk.

## Source References

- Azure Retail Prices API: <https://prices.azure.com/api/retail/prices>
- Azure Container Apps pricing: <https://azure.microsoft.com/pricing/details/container-apps/>
- Azure Database for PostgreSQL Flexible Server pricing: <https://azure.microsoft.com/pricing/details/postgresql/flexible-server/>
- Azure Service Bus pricing: <https://azure.microsoft.com/pricing/details/service-bus/>
- Azure Blob Storage pricing: <https://azure.microsoft.com/pricing/details/storage/blobs/>
- Azure Key Vault pricing: <https://azure.microsoft.com/pricing/details/key-vault/>
- Azure Monitor pricing: <https://azure.microsoft.com/pricing/details/monitor/>
- Azure Virtual Machines pricing: <https://azure.microsoft.com/pricing/details/virtual-machines/linux/>
- AWS pricing: <https://aws.amazon.com/pricing/>
- AWS Fargate pricing: <https://aws.amazon.com/fargate/pricing/>
- Google Cloud pricing: <https://cloud.google.com/pricing/list>
- Oracle Cloud pricing: <https://www.oracle.com/cloud/pricing/>
