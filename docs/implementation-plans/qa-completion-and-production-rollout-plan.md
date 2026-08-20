# QA Completion and Production Rollout Plan

Owner: Marius Bloemhof (PVM).
Author: integration team.
Created: 2026-08-20.
Status: active.

This plan answers one question: what must happen, in what order, to move the
Shoprite invoice integration from the current QA state to automatic production
submission.

Two other documents stay authoritative for design:

- `docs/implementation-plans/shoprite-production-automation-plan.md` for the
  architecture and the slice definitions.
- `docs/implementation-plans/integration-admin-console-plan.md` for the console.

This plan is the schedule and the checklist.

## 1. Where The Project Stands On 2026-08-20

Built, deployed in QA, and verified:

- Shoprite PO inbox, scheduled refresh, and freshness alerting.
- Acumatica invoice discovery through a push notification and through scheduled
  incremental reconciliation.
- Candidate matching, mapping enrichment, validation, and XML generation.
- Concurrency-safe submission operations, frozen payloads, and at-most-once
  external send.
- Immutable payload archive with SHA-256 verification.
- Service Bus queues, transactional outbox, and worker runtime.
- Global inventory mappings with an Admin exception workflow.
- Automation policy with `Disabled`, `Shadow`, `Allowlisted`, and `Enabled`
  modes, allowlists, caps, and an audited emergency stop.
- Exception operations: ambiguous resolution, hold and release, safe retry,
  guarded dead-letter replay, and the Admin exception console.

Proven by live evidence:

- Invoice `INV158888` and `INV158889` were submitted to Shoprite QA and accepted.
- Shoprite confirmed on 2026-07-27 that the payload is structurally sound.
- The emergency stop blocks the automatic path and the manual path. Both
  refusals are audited.
- Automatic submission has never run. The submit-command count is zero.

Current QA numbers:

- Automation policy v7, mode `Disabled`, emergency stop clear.
- 231 Shoprite POs, 4 invoice candidates, 3 submitted invoices.
- 229 open dead-letter exceptions from the incidents of 5 to 7 August.

Not built:

- Production infrastructure. `rg-pvm-integrations-qa` is the only resource group.
- Production release pipeline and approval gate.

## 2. The Two Blockers

Only two things prevent production. Everything else is scheduled work.

1. **Shoprite production credentials and contract details.** Nobody at PVM can
   produce these. The request must go to Shoprite now.
2. **Acumatica production access.** The instance, a least-privilege integration
   user, and permission to create the Generic Inquiry and the Push Notification.

Engineering is not the constraint. Section 6 lists the exact requests.

## 3. Part One: Complete QA

Goal: every go-live gate that can be proven without production is proven and
recorded.

| # | Task | Who | Method | Gate it satisfies |
|---|---|---|---|---|
| A1 | Clear the 229 historical dead letters | Marius, one click | Exception console, bulk resolution | Exception center operational |
| A2 | Induced rejection test | Team, one submit click by Marius | Submit a deliberately invalid invoice to Shoprite QA, then work the `Rejected` queue and the safe retry | Production response parser recognises rejection |
| A3 | Induced ambiguous test | Team | Stop the worker inside the send boundary, then resolve the `Ambiguous` outcome with evidence | Ambiguous resolution operational |
| A4 | Poison message and replay test | Team | Queue a malformed command, confirm the dead letter, then replay it through the console | Service Bus, DLQ, and replay operational |
| A5 | Concurrency proof | Team | Repeat the ten-run submission stress test on the current build | One external POST under concurrency |
| A6 | PostgreSQL restore test | Team, cost approval by Marius | Point-in-time restore to a temporary server, verify candidates, operations, and archives | Restore test passes |
| A7 | Blob retrieval test | Team | Read and hash-verify an archived payload from the restore | Payload archive complete |
| A8 | Revision rollback test | Team | Roll the API back one revision, verify no submission replay, then restore | Previous revision restorable |
| A9 | Log redaction check | Team | Inspect QA logs for credentials and payload bodies | No credentials in logs |
| A10 | Alert and on-call check | Team, recipients from Marius | Fire the freshness alert, confirm delivery to the recipients | Monitoring active |

Exit criterion for Part One: A1 to A10 pass, and the evidence is recorded in
`docs/status/current-project-status.md`.

Estimate: 3 working days once A1 is done, excluding the wait for cost approval
on A6.

## 4. Part Two: Build Production

Goal: a production estate that is isolated from QA and that starts disabled.

This is Slice 9 of the automation plan. It needs no Shoprite input, so it runs
in parallel with Part One.

Four decisions are needed from Marius before the work starts:

1. Subscription and resource group name for production.
2. Region. QA uses `southafricanorth`.
3. Network restriction level. Public ingress with authentication, or restricted
   ingress with a private database path.
4. Backup retention and the recovery point objective.

Then the work is:

| # | Task | Output |
|---|---|---|
| B1 | Parameterise the Bicep for two environments | One template, two parameter files, no shared secrets |
| B2 | Provision the production estate | Container Apps, PostgreSQL, Service Bus, Blob, Key Vault, ACR, monitoring |
| B3 | Environment-scoped deployment workflow | A GitHub environment with a required approval before any production deploy |
| B4 | Seed the production policy as `Disabled` | The migration seeds mode `Disabled` with the emergency stop clear |
| B5 | Backup and restore rehearsal in production | A restore test recorded before any real data exists |
| B6 | Production runbook | Deploy, rollback, kill switch, and escalation |

Exit criterion for Part Two: production deploys from `main` through an approved
release, serves `/health`, refuses anonymous API calls, and reports automation
`Disabled`.

Estimate: 2 working days after the four decisions.

## 5. Part Three: The Five Production Gates

Each gate has an entry condition, an exit condition, and a named approver. No
gate may be skipped. Marius approves every gate.

### Gate 1: Connectivity, no sends

Entry: Part Two complete, and the Shoprite and Acumatica inputs received.

Actions:

1. Load production credentials into production Key Vault.
2. Verify the Acumatica production connection read-only. Confirm login, the
   endpoint version, the account scope, and the last-modified field.
3. Create the production Generic Inquiry and Push Notification, and leave the
   destination inactive until the webhook path is verified.
4. Verify Shoprite production authentication and a `VendorOrder` read. Perform no
   invoice POST.
5. Confirm that production logs contain no credentials.

Exit: production discovers real POs and real finalized invoices, prepares
candidates, and sends nothing. Automation stays `Disabled`.

Estimate: 1 to 2 days after the inputs arrive.

### Gate 2: Shadow

Entry: Gate 1 passed.

Actions:

1. Set production automation to `Shadow`.
2. Run for at least five normal business days.
3. Each day, compare the candidate set, mappings, totals, and expected timing
   against Acumatica.
4. Resolve every unexpected `NeedsReview` case through the console.
5. Suppress a webhook event on purpose and prove that reconciliation still finds
   the invoice.

Exit: five clean business days, zero submit commands, zero unexplained
exceptions.

Estimate: 5 to 7 business days. This duration is fixed by observation, not by
effort.

### Gate 3: Allowlisted canary

Entry: Gate 2 passed, and Shoprite support is available.

Actions:

1. Approve one account and delivery-location cohort.
2. Set mode `Allowlisted`, daily cap 1, normal order types only, inside the
   agreed business-hours window.
3. Release exactly one invoice automatically.
4. Confirm the result independently in Acumatica, in the console, and with
   Shoprite.
5. Raise the daily cap in small steps only after each day is clean.
6. Stop immediately on any ambiguous outcome, duplicate, totals anomaly, or
   reconciliation gap.

Exit: at least five automatically submitted invoices, each independently
confirmed, and no unresolved exception.

Estimate: 5 business days.

### Gate 4: Controlled expansion

Entry: Gate 3 passed.

Actions:

1. Add cohorts explicitly, one group at a time.
2. Review the exception rate and the submission latency after each addition.
3. Keep unsupported order types and catch-weight items on the manual path.

Exit: the full intended account and location scope runs automatically for five
business days without an unexplained exception.

Estimate: 5 to 10 business days.

### Gate 5: Enabled

Entry: Gate 4 passed and written approval from Marius.

Actions:

1. Set mode `Enabled`.
2. Keep the daily cap and the business-hours window until the first full week
   passes.
3. Record the approval, the date, and the operating parameters in the status
   document.

Exit: automatic submission runs for all configured eligible invoices. The kill
switch and the manual path remain permanent.

## 6. Inputs To Request Now

Send these requests on 2026-08-20. The reply time controls the whole schedule.

### From Shoprite

| # | Item | Received |
|---|---|---|
| S1 | Production `VendorOrder` and `VendorInvoice` base URL | No |
| S2 | Production username, password, and activation date | No |
| S3 | Confirmation of production supplier GLN `6001197000006` and VAT `4010137059` | No |
| S4 | Success and rejection response examples | No |
| S5 | Rate limits and maintenance windows | No |
| S6 | Duplicate and replay behaviour for `VendorInvoice` | No |
| S7 | Source address allowlisting requirements | No |
| S8 | Production support and escalation contacts | No |

### From Acumatica and About IT Group

| # | Item | Received |
|---|---|---|
| I1 | Production instance URL, tenant, company, and branch | No |
| I2 | Endpoint name and version, with the exposed fields | No |
| I3 | A dedicated least-privilege integration user | No |
| I4 | Permission to create the Generic Inquiry and the Push Notification | No |
| I5 | Confirmation of a reliable last-modified field | No |
| I6 | Outbound webhook network path and source addresses | No |
| I7 | Upgrade and change-window ownership | No |

### From PVM

| # | Item | Received |
|---|---|---|
| P1 | Production subscription, resource group, and region | No |
| P2 | Network restriction level and backup retention | No |
| P3 | First-release account and delivery-location allowlist | No |
| P4 | Supported order types for the first release | No |
| P5 | Canary window in business hours | No |
| P6 | Alert recipients and operational Admins | No |
| P7 | Final go-live approver | No |

## 7. Schedule

The dates assume that the Shoprite and Acumatica inputs arrive by 2026-09-01.
Every production date moves with those inputs.

| Period | Work | Depends on |
|---|---|---|
| 2026-08-20 to 2026-08-26 | Part One: complete QA evidence | A1 click, A6 cost approval |
| 2026-08-24 to 2026-08-28 | Part Two: build production | P1 and P2 |
| Week of 2026-09-01 | Gate 1: connectivity, no sends | S1 to S3, I1 to I4 |
| 2026-09-08 to 2026-09-12 | Gate 2: shadow, five business days | Gate 1 |
| Week of 2026-09-15 | Gate 3: allowlisted canary | Gate 2, S8, P3 to P5 |
| 2026-09-22 to 2026-10-02 | Gate 4: controlled expansion | Gate 3 |
| Early October 2026 | Gate 5: enabled | Gate 4, written approval |

## 8. Risks

| Risk | Effect | Response |
|---|---|---|
| Shoprite input is slow | The whole production schedule slips | Request on 2026-08-20 and escalate weekly. Parts One and Two continue regardless |
| Shoprite production behaviour differs from QA | Rejections or ambiguous outcomes at canary | The canary starts at one invoice per day with independent confirmation |
| Acumatica webhook cannot reach production | Discovery latency rises | Scheduled reconciliation already covers completeness. Shadow proves it |
| Acumatica upgrade during rollout | Endpoint or field changes | Confirm change-window ownership in I7 before Gate 2 |
| An uncertain send in production | An invoice may or may not have reached Shoprite | No automatic retry. The console requires evidence before any resolution |
| Unmapped new products | Invoices stop at `NeedsReview` | The mapping exception queue already handles this. Review it daily during Gates 2 and 3 |

## 9. Rollback Position

Rollback stops new automatic sends. It never reverses an invoice that Shoprite
has already accepted.

1. Set automation to `Disabled`, or activate the emergency stop.
2. Stop the submission queue consumers.
3. Leave discovery and reconciliation running.
4. Preserve queue messages, operations, attempts, and archived payloads.
5. Classify in-flight `Sending` operations as `Ambiguous`.
6. Restore the previous application revision only after the state is preserved.
7. Resume manual submission for validated candidates when approved.

Never purge a queue and never delete an attempt during a rollback.

## 10. Definition Of Done

Production automation is complete when all of the following are true:

- The five gates passed, each with a recorded approval.
- Automatic submission runs for the agreed scope.
- The kill switch, the manual path, and the exception console all work in
  production.
- The payload archive, audit trail, and run history cover every production
  submission.
- Alerts reach the named recipients.
- The production runbook and the escalation contacts are approved.
