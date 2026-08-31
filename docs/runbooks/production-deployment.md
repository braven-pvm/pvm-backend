# Production Deployment Runbook

The production estate is separate from QA in every respect: its own resource
group, database, queues, storage, registry, Key Vault, and credentials.

| Item | QA | Production |
|---|---|---|
| Resource group | `rg-pvm-integrations-qa` | `rg-pvm-integrations-prod` |
| Key Vault | `kv-pvm-intg-qa` | `kv-pvm-intg-prod` |
| Registry | `acrpvmintegrationsqa` | `acrpvmintegrationsprod` |
| Database | `psql-pvm-integrations-qa` | `psql-pvm-integrations-prod` |
| Workflow | `Deploy QA`, automatic on `main` | `Deploy Production`, manual with approval |
| Image tag | `qa-<sha>` | `prod-<sha>` |

Both environments run in subscription `Azure subscription 1`
(`1d0e7292-24e5-425e-870b-c56904b70da6`), region `southafricanorth`.

## Safety position

Production starts inert:

- Automation mode `Disabled`, seeded by the first migration.
- `Shoprite:InvoiceSubmissionMode` is `LocalStub`, so no production invoice can
  reach Shoprite even by a manual click.
- `Shoprite:AcknowledgeOrders` is `false`, so the order feed is untouched.
- `Acumatica:InvoiceSourceMode` is `Fixture`, so no live invoice is read.

Each of the three settings is a GitHub environment variable. Gate 1 changes the
Acumatica mode, Gate 2 leaves sending off, and Gate 3 is the first change that
allows a real submission.

## Before the first run

1. **Entra app registrations.** Create the production API and workbench
   registrations, mirroring QA. Record the tenant, both client identifiers, the
   workbench client secret, and the API scope.
2. **Key Vault secrets.** `kv-pvm-intg-prod` must contain:

   | Secret | Status |
   |---|---|
   | `shoprite--baseurl` | Present |
   | `shoprite--username` | Present |
   | `shoprite--password` | Present |
   | `shoprite--contractid` | Present |
   | `postgres--adminpassword` | Present, generated |
   | `auth--tenantid` | Required |
   | `auth--api-clientid` | Required |
   | `auth--workbench-clientid` | Required |
   | `auth--workbench-clientsecret` | Required |
   | `auth--apiscope` | Required |
   | `auth--nextauthsecret` | Required |
   | `auth--bootstrapadminemails` | Required |
   | `auth--bootstrapadminobjectids` | Required |
   | `acumatica--username` | Required |
   | `acumatica--password` | Required |
   | `acumatica--webhooksecret` | Required |

   The workflow stops before it changes anything when one is missing.
3. **GitHub environment `production`** with the deployer as a required
   reviewer, and these variables:

   | Variable | First value |
   |---|---|
   | `PROD_WORKBENCH_URL` | Empty until the first deploy reveals the domain |
   | `PROD_SHOPRITE_SUBMISSION_MODE` | `LocalStub` |
   | `PROD_SHOPRITE_ACKNOWLEDGE_ORDERS` | `false` |

4. **Acumatica production access.** The instance URL, company, endpoint version,
   and a least-privilege integration user.

## Running a deployment

1. Open the **Deploy Production** workflow.
2. Type `DEPLOY PRODUCTION` in the confirmation input.
3. Approve the environment gate.

The workflow runs the full backend and workbench verification, ensures the
registry exists, builds and pushes the three images, deploys the Bicep template,
applies the database migrations, and smoke tests the result.

## The first deployment takes two passes

The Container Apps domain suffix does not exist until the environment is
created, so:

1. Run the workflow with `PROD_WORKBENCH_URL` empty.
2. Read the workbench address:

```bash
az containerapp show -g rg-pvm-integrations-prod -n ca-pvm-workbench-prod --query "properties.configuration.ingress.fqdn" -o tsv
```

3. Set `PROD_WORKBENCH_URL` to `https://<that address>`.
4. Add the same address plus `/api/auth/callback/azure-ad` as a redirect URI on
   the workbench app registration.
5. Run the workflow again.

## After a deployment

Confirm all of the following:

- API `/health` returns `200`.
- The invoice API returns `401` without a token.
- The workbench redirects to sign-in.
- The automation console reports mode `Disabled` with the emergency stop clear.
- The three Service Bus queues hold no messages.
- `__EFMigrationsHistory` contains every migration.

## Rollback

1. Set automation to `Disabled`, or activate the emergency stop.
2. Restore the previous revision:

```bash
az containerapp revision list -g rg-pvm-integrations-prod -n ca-pvm-api-prod -o table
az containerapp ingress traffic set -g rg-pvm-integrations-prod -n ca-pvm-api-prod --revision-weight <previous>=100
```

3. Never purge a queue and never delete a submission attempt during a rollback.

## Known debt

`deploy-prod.yml` duplicates `deploy-qa.yml` rather than sharing a reusable
workflow. The duplication was deliberate, to avoid changing the working QA
pipeline while production is built. Merge them once production is stable.
