# Gate 1: Production Connectivity, No Sends

Goal: production reads real data and sends nothing. This runbook is written so
the whole gate can run in one sitting once the Acumatica user exists.

Nothing here submits an invoice. Nothing here acknowledges a Shoprite order.

## Already done

| Item | State |
|---|---|
| Resource group `rg-pvm-integrations-prod` | Created |
| Key Vault `kv-pvm-intg-prod` | Created, RBAC, purge protection |
| Entra registrations, scope, consent, service principals | Created |
| 14 of 16 Key Vault secrets | Stored |
| Shoprite production connectivity | Proven on 2026-08-31: `GET VendorOrder` returned `200` with 9 real orders |
| Acumatica production endpoint | Confirmed reachable, build `25.201.0213`, `Default 24.200.001` available |
| GitHub environment `production` | Created with a required reviewer |

## Step 1: Store the Acumatica user

```bash
az keyvault secret set --vault-name kv-pvm-intg-prod --name acumatica--username --value "<user>"
az keyvault secret set --vault-name kv-pvm-intg-prod --name acumatica--password --file <file-with-password>
```

Use `--file` for the password so it never appears in shell history.

Verify all sixteen secrets exist:

```bash
az keyvault secret list --vault-name kv-pvm-intg-prod --query "sort_by([].{n:name},&n)[].n" -o tsv
```

The deployment refuses to change anything while one is missing.

## Step 2: Confirm the environment variables

| Variable | Value for Gate 1 | Why |
|---|---|---|
| `PROD_SHOPRITE_SUBMISSION_MODE` | `LocalStub` | No invoice can reach Shoprite |
| `PROD_SHOPRITE_ACKNOWLEDGE_ORDERS` | `false` | People accept orders on the portal |
| `PROD_ACUMATICA_SOURCE_MODE` | `Fixture` | No live invoice is read on the first pass |
| `PROD_ACUMATICA_INVOICE_DATE_FROM` | Not set yet | Set it in step 6 |
| `PROD_WORKBENCH_URL` | Not set yet | Set it in step 4 |

## Step 3: First deployment

Run **Deploy Production**, type `DEPLOY PRODUCTION`, and approve the gate.

The workflow verifies the build, ensures the registry, pushes the images,
deploys the template, applies the migrations, then smoke tests.

## Step 4: Close the two-pass loop

```bash
az containerapp show -g rg-pvm-integrations-prod -n ca-pvm-workbench-prod --query "properties.configuration.ingress.fqdn" -o tsv
```

1. Set `PROD_WORKBENCH_URL` to `https://<that address>`.
2. Add `https://<that address>/api/auth/callback/azure-ad` as a redirect URI on
   the `pvm-backend-prod-workbench` registration.
3. Run the workflow again.

## Step 5: Verify the estate

- API `/health` returns `200`.
- The invoice API returns `401` without a token.
- The workbench redirects to sign-in, and you can sign in.
- The automation console reports mode `Disabled` with the emergency stop clear.
- All three Service Bus queues are empty.
- `__EFMigrationsHistory` contains every migration.

## Step 6: Turn on live reading

Caution: choose the cutover date before this step. Without one the integration
refuses to start, which is deliberate. A wrong date imports history.

1. Set `PROD_ACUMATICA_INVOICE_DATE_FROM` to the go-live cutover, in the same
   shape as QA: `2026-09-01T00:00:00+02:00`.
2. Set `PROD_ACUMATICA_SOURCE_MODE` to `Real`.
3. Run the workflow again.

## Step 7: Prove the reads, prove the silence

Reads:

- The Shoprite PO inbox fills from the production `VendorOrder` feed.
- The invoice queue fills with finalized invoices dated on or after the cutover.
- Candidates match their Shoprite PO and reach `Ready` or `NeedsReview`.

Silence, all of which must hold:

```sql
select count(*) from integration_outbox_messages where "MessageType" ilike '%submit%';   -- 0
select count(*) from submission_operations;                                              -- 0
select count(*) from invoice_submission_attempts;                                        -- 0
select "Mode", "EmergencyStop" from automation_policy_versions order by "Version" desc limit 1;  -- Disabled, false
```

Gate 1 passes when production discovers real invoices, matches them to real
purchase orders, and every one of those counts is zero.

## Step 8: Record it

Add the evidence to `docs/status/current-project-status.md`: the deployment
run, the image tag, the revision names, the candidate and purchase-order
counts, and the four zero counts above.

## Rollback

Set `PROD_ACUMATICA_SOURCE_MODE` back to `Fixture` and redeploy. Production then
reads nothing live. The estate stays, and no data is lost.
