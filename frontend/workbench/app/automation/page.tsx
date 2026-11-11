import { changeAutomationPolicyAction, setAutomationEmergencyStopAction } from "../actions";
import { getAutomationPolicy, type AutomationMode, type AutomationPolicyView } from "../../src/api/client";
import { hasAnyRole, requireWorkbenchUser } from "../../src/auth/session";
import { formatDateTime } from "../../src/formatters.mjs";
import {
  CheckboxField,
  ConfirmDialog,
  DataTable,
  EmptyState,
  FormActions,
  FormLayout,
  KeyValueList,
  Notice,
  Panel,
  RadioGroup,
  RecordPage,
  StatTile,
  StatusBadge,
  SubmitButton,
  TextareaField,
  TextField,
  TileGrid,
  Timeline,
  type Column,
} from "../../src/ui";

export const dynamic = "force-dynamic";
export const metadata = { title: "Shoprite submission automation" };

type AutomationPageProps = {
  searchParams: Promise<{ policyError?: string; policyStatus?: string }>;
};

const modes: Array<{ value: AutomationMode; label: string; hint: string }> = [
  { value: "Disabled", label: "Disabled", hint: "Invoices are read and checked. Nothing is sent automatically." },
  { value: "Shadow", label: "Shadow", hint: "Each invoice gets a decision and a report. Nothing is sent automatically." },
  { value: "Allowlisted", label: "Allowlisted", hint: "Only invoices for the accounts and delivery locations below are sent." },
  { value: "Enabled", label: "Enabled", hint: "Every invoice that passes the checks is sent." },
];

type Decision = AutomationPolicyView["recentDecisions"][number];

const decisionColumns: Column<Decision>[] = [
  { key: "invoice", header: "Invoice", render: (decision) => decision.invoiceNumber, mono: true },
  { key: "outcome", header: "Decision", render: (decision) => <StatusBadge status={decision.outcome} tone={decision.outcome === "Excluded" ? "warning" : "info"} /> },
  { key: "summary", header: "Why", render: (decision) => decision.summary },
  { key: "policy", header: "Policy", render: (decision) => `v${decision.policyVersion}`, align: "end" },
  { key: "evaluated", header: "Decided", render: (decision) => formatDateTime(decision.evaluatedAt), weak: true },
];

export default async function AutomationPage({ searchParams }: AutomationPageProps) {
  const user = await requireWorkbenchUser("/automation");
  const isAdmin = hasAnyRole(user, ["Admin"]);
  const params = await searchParams;
  const view = await getAutomationPolicy();
  const policy = view.policy;
  const summary = view.decisionSummary;

  return (
    <RecordPage
      header={{
        eyebrow: "Shoprite · automation",
        title: "Shoprite submission",
        description: `Which checked invoices may be sent to Shoprite without a person. ${view.environmentName}, policy v${policy.version}.`,
        actions: (
          <>
            {policy.emergencyStop ? <StatusBadge status="Blocked" label="Emergency stop" /> : <StatusBadge status={policy.mode} />}
            {isAdmin ? (
              <ConfirmDialog
                triggerLabel={policy.emergencyStop ? "Clear emergency stop" : "Stop all submissions"}
                triggerVariant={policy.emergencyStop ? "primary" : "secondary"}
                title={policy.emergencyStop ? "Clear the emergency stop?" : "Stop all Shoprite submissions?"}
                confirmLabel={policy.emergencyStop ? "Clear emergency stop" : "Stop all submissions"}
                pendingLabel="Saving…"
                tone={policy.emergencyStop ? "default" : "danger"}
                action={setAutomationEmergencyStopAction}
                fields={{ expectedVersion: String(policy.version), active: policy.emergencyStop ? "false" : "true" }}
              >
                <p>
                  {policy.emergencyStop
                    ? "New submissions are allowed again, by hand and by the automation, under the current policy."
                    : "Every new submission stops at once, by hand and by the automation. Reading and checking invoices continues."}
                </p>
                <TextField name="reason" label="Reason" hint="An incident, test or clearance reference" width="full" required />
              </ConfirmDialog>
            ) : null}
          </>
        ),
      }}
      notice={
        <>
          {params.policyError ? (
            <Notice tone="error" title="Nothing changed" live>
              {params.policyError}
            </Notice>
          ) : null}
          {params.policyStatus ? <Notice tone="success" title={params.policyStatus} live /> : null}
          {policy.emergencyStop ? (
            <Notice tone="error" title="Emergency stop: every submission is blocked">
              Submissions by hand and by the automation are refused. Reading and checking invoices continues.
            </Notice>
          ) : null}
        </>
      }
      summary={
        <KeyValueList
          items={[
            { label: "Mode", value: policy.mode },
            { label: "Accounts", value: policy.accountAllowlist.join(", ") || "Any", mono: true },
            { label: "Locations", value: policy.locationAllowlist.join(", ") || "Any", mono: true },
            { label: "Order types", value: policy.supportedOrderTypes.join(", "), mono: true },
            { label: "Window", value: `${policy.automaticWindowStart.slice(0, 5)}–${policy.automaticWindowEnd.slice(0, 5)} ${policy.timeZoneId}` },
            { label: "Daily cap", value: policy.dailyAutomaticSubmissionCap === 0 ? "No cap" : `${policy.dailyAutomaticSubmissionCap} invoices` },
            { label: "Changed", value: `${formatDateTime(policy.createdAt)} by ${policy.createdBy}` },
          ]}
        />
      }
      activity={
        <Timeline
          label="Policy history"
          entries={view.recentVersions.map((version) => ({
            id: String(version.version),
            time: formatDateTime(version.createdAt),
            dateTime: version.createdAt,
            title: `v${version.version}: ${version.mode}${version.emergencyStop ? ", emergency stop" : ""}`,
            detail: `${version.reason} · ${version.createdBy}`,
          }))}
        />
      }
    >
      <TileGrid>
        <StatTile label="Checked" value={String(summary.evaluated)} detail="Invoices with a decision" />
        <StatTile label="Would send" value={String(summary.wouldSubmit)} detail={policy.mode === "Shadow" ? "Shadow mode sends nothing" : "Allowed by the policy"} />
        <StatTile label="Queued to send" value={String(summary.queued)} />
        <StatTile label="Excluded" value={String(summary.excluded)} detail="Blocked by a check or the policy" tone={summary.excluded > 0 ? "warning" : "default"} />
      </TileGrid>

      <Panel flush title="Recent decisions" description="The latest decision for each invoice, newest first.">
        <DataTable
          caption="Recent automation decisions"
          columns={decisionColumns}
          rows={view.recentDecisions}
          rowKey={(decision) => decision.id}
          rowHref={(decision) => `/invoices/${decision.invoiceCandidateId}`}
          empty={<EmptyState title="No decisions yet" description="Each invoice gets a decision when it is read from Acumatica." />}
        />
      </Panel>

      {isAdmin ? (
        <Panel title="Change the policy" description="Each change makes a new version, goes in the audit log and applies to current invoices at once.">
          <form action={changeAutomationPolicyAction}>
            <input type="hidden" name="expectedVersion" value={policy.version} />
            <FormLayout>
              <RadioGroup name="mode" label="Mode" required defaultValue={policy.mode} options={modes} />
              <TextareaField
                name="accountAllowlist"
                label="Shoprite accounts"
                hint="One per line. Allowlisted mode sends only when the account and the location both match"
                rows={3}
                defaultValue={policy.accountAllowlist.join("\n")}
              />
              <TextareaField name="locationAllowlist" label="Delivery locations" hint="One per line" rows={3} defaultValue={policy.locationAllowlist.join("\n")} />
              <TextField name="supportedOrderTypes" label="Order types" hint="Separate with commas, for example 220, 258" width="m" required defaultValue={policy.supportedOrderTypes.join(", ")} />
              <TextField name="stabilizationDelayMinutes" label="Wait after an invoice changes (minutes)" type="number" min={0} width="s" required defaultValue={String(policy.stabilizationDelayMinutes)} />
              <TextField name="purchaseOrderFreshnessMinutes" label="Maximum purchase order age (minutes)" type="number" min={1} width="s" required defaultValue={String(policy.purchaseOrderFreshnessMinutes)} />
              <TextField name="acumaticaFreshnessMinutes" label="Maximum Acumatica data age (minutes)" type="number" min={1} width="s" required defaultValue={String(policy.acumaticaFreshnessMinutes)} />
              <TextField name="dailyAutomaticSubmissionCap" label="Most invoices sent in one day" hint="0 means no cap" type="number" min={0} width="s" required defaultValue={String(policy.dailyAutomaticSubmissionCap)} />
              <TextField name="automaticWindowStart" label="Sending starts at" type="time" width="s" required defaultValue={policy.automaticWindowStart.slice(0, 5)} />
              <TextField name="automaticWindowEnd" label="Sending ends at" hint="The same start and end time allows the whole day" type="time" width="s" required defaultValue={policy.automaticWindowEnd.slice(0, 5)} />
              <TextField name="timeZoneId" label="Time zone" width="m" required defaultValue={policy.timeZoneId} />
              <TextField name="reason" label="Reason for the change" hint="The operational reason and a change reference" width="full" required />
              <Notice tone="warning" title="Enabled mode needs three confirmations">
                <p>
                  Type the environment name, type ENABLE {view.environmentName}, and tick the box. Without all three, the
                  change to Enabled is refused.
                </p>
              </Notice>
              <TextField name="environmentConfirmation" label="Environment name" width="m" placeholder={view.environmentName} />
              <TextField name="typedConfirmation" label="Confirmation" width="m" placeholder={`ENABLE ${view.environmentName}`} />
              <CheckboxField name="acknowledgeAutomaticSubmissions" value="true" label="I understand that invoices that pass the checks are sent automatically" />
              <FormActions>
                <SubmitButton pendingLabel="Saving…">Save policy</SubmitButton>
              </FormActions>
            </FormLayout>
          </form>
        </Panel>
      ) : (
        <Notice tone="info" title="Only an Admin can change the policy">
          You can see the policy and its decisions. Ask an Admin to change it.
        </Notice>
      )}

      <Panel title="Rules that never change">
        <KeyValueList
          layout="stacked"
          items={[
            { label: "Shadow and Disabled", value: "Never send anything automatically." },
            { label: "One path", value: "Automatic and manual submissions use the same command, which refuses a duplicate." },
            { label: "Unknown outcomes", value: "Are never retried automatically. A person checks Shoprite first." },
            { label: "Fresh data", value: "The invoice is read from Acumatica again just before an automatic send." },
          ]}
        />
      </Panel>
    </RecordPage>
  );
}
