import Link from "next/link";
import {
  changeAutomationPolicyAction,
} from "../actions";
import { getAutomationPolicy, type AutomationMode } from "../../src/api/client";
import { hasAnyRole, requireWorkbenchUser } from "../../src/auth/session";
import { EmergencyStopForm } from "./emergency-stop-form";

export const dynamic = "force-dynamic";

type AutomationPageProps = {
  searchParams: Promise<{ policyError?: string; policyStatus?: string }>;
};

const modes: Array<{ value: AutomationMode; label: string; consequence: string }> = [
  { value: "Disabled", label: "Disabled", consequence: "Discover only. Automatic sends are blocked." },
  { value: "Shadow", label: "Shadow", consequence: "Evaluate and report. No automatic sends." },
  { value: "Allowlisted", label: "Allowlisted", consequence: "Send only approved account and location cohorts." },
  { value: "Enabled", label: "Enabled", consequence: "Send every eligible configured invoice." },
];

export default async function AutomationPage({ searchParams }: AutomationPageProps) {
  const user = await requireWorkbenchUser("/automation");
  const isAdmin = hasAnyRole(user, ["Admin"]);
  const params = await searchParams;
  const view = await getAutomationPolicy();
  const policy = view.policy;

  return (
    <main className="page-shell automation-page">
      <section className="page-heading">
        <div>
          <h1>Automation</h1>
          <p>Control which validated invoices may enter the automatic Shoprite submission path.</p>
        </div>
        <div className="mode-summary">
          <span>{view.environmentName} · policy v{policy.version}</span>
          <strong className={`mode-${policy.mode.toLowerCase()}`}>
            {policy.emergencyStop ? "Emergency stop active" : `Automation ${policy.mode}`}
          </strong>
        </div>
      </section>

      {params.policyError ? (
        <div className="alert-banner alert-error" role="alert">
          <strong>Configuration not changed</strong><span>{params.policyError}</span>
        </div>
      ) : null}
      {params.policyStatus ? (
        <div className="alert-banner alert-success" role="status">
          <strong>Configuration updated</strong><span>{params.policyStatus}</span>
        </div>
      ) : null}

      <section className={`emergency-control ${policy.emergencyStop ? "is-active" : ""}`} aria-label="Emergency stop">
        <div>
          <span className="section-kicker">Submission safety</span>
          <h2>{policy.emergencyStop ? "All submission claims are stopped" : "Emergency stop is clear"}</h2>
          <p>
            {policy.emergencyStop
              ? "Manual and automatic submissions are blocked. Discovery and reconciliation continue."
              : "Activate only when all new manual and automatic Shoprite submissions must stop immediately."}
          </p>
        </div>
        {isAdmin ? (
          <EmergencyStopForm active={policy.emergencyStop} policyVersion={policy.version} />
        ) : <span className="read-only-note">Admin access required to change this control.</span>}
      </section>

      <section className="metric-strip four-up" aria-label="Current policy decisions">
        <div><span>Evaluated</span><strong>{view.decisionSummary.evaluated}</strong></div>
        <div><span>Would submit</span><strong>{view.decisionSummary.wouldSubmit}</strong></div>
        <div><span>Queued</span><strong>{view.decisionSummary.queued}</strong></div>
        <div><span>Excluded</span><strong>{view.decisionSummary.excluded}</strong></div>
      </section>

      <section className="automation-layout">
        <div className="automation-main">
          <section className="detail-panel policy-panel">
            <div className="section-heading">
              <h2>Submission policy</h2>
              <p>Changes are versioned, audited and applied to current candidates immediately.</p>
            </div>
            <form action={changeAutomationPolicyAction} className="policy-form">
              <input name="expectedVersion" type="hidden" value={policy.version} />
              <fieldset disabled={!isAdmin}>
                <legend>Automation mode</legend>
                <div className="mode-options">
                  {modes.map((mode) => (
                    <label key={mode.value} className="mode-option">
                      <input defaultChecked={policy.mode === mode.value} name="mode" type="radio" value={mode.value} />
                      <span><strong>{mode.label}</strong><small>{mode.consequence}</small></span>
                    </label>
                  ))}
                </div>
              </fieldset>

              <div className="policy-section">
                <div><h3>Scope</h3><p>Allowlisted mode requires both an account and delivery-location match.</p></div>
                <div className="policy-fields two-column">
                  <label><span>Shoprite accounts</span><textarea defaultValue={policy.accountAllowlist.join("\n")} name="accountAllowlist" rows={4} /></label>
                  <label><span>Delivery locations</span><textarea defaultValue={policy.locationAllowlist.join("\n")} name="locationAllowlist" rows={4} /></label>
                  <label><span>Supported order types *</span><input defaultValue={policy.supportedOrderTypes.join(", ")} name="supportedOrderTypes" required /></label>
                </div>
              </div>

              <div className="policy-section">
                <div><h3>Timing and freshness</h3><p>Zero daily cap means unlimited. Equal start and end times allow the full day.</p></div>
                <div className="policy-fields compact-grid">
                  <NumberField label="Stabilization delay" name="stabilizationDelayMinutes" value={policy.stabilizationDelayMinutes} suffix="minutes" min={0} />
                  <NumberField label="Maximum PO age" name="purchaseOrderFreshnessMinutes" value={policy.purchaseOrderFreshnessMinutes} suffix="minutes" min={1} />
                  <NumberField label="Maximum Acumatica age" name="acumaticaFreshnessMinutes" value={policy.acumaticaFreshnessMinutes} suffix="minutes" min={1} />
                  <NumberField label="Daily automatic cap" name="dailyAutomaticSubmissionCap" value={policy.dailyAutomaticSubmissionCap} suffix="invoices" min={0} />
                  <label><span>Window starts</span><input defaultValue={timeInput(policy.automaticWindowStart)} name="automaticWindowStart" required type="time" /></label>
                  <label><span>Window ends</span><input defaultValue={timeInput(policy.automaticWindowEnd)} name="automaticWindowEnd" required type="time" /></label>
                  <label><span>Time zone *</span><input defaultValue={policy.timeZoneId} name="timeZoneId" required /></label>
                </div>
              </div>

              <div className="policy-section enable-confirmation">
                <div><h3>Change approval</h3><p>Unrestricted Enabled mode is rejected unless every confirmation is supplied.</p></div>
                <div className="policy-fields two-column">
                  <label className="full-field"><span>Change reason *</span><input name="reason" placeholder="Operational reason and change reference" required /></label>
                  <label><span>Environment confirmation</span><input name="environmentConfirmation" placeholder={view.environmentName} /></label>
                  <label><span>Typed confirmation</span><input name="typedConfirmation" placeholder={`ENABLE ${view.environmentName}`} /></label>
                  <label className="check-row acknowledgement"><input name="acknowledgeAutomaticSubmissions" type="checkbox" value="true" /><span>I acknowledge that eligible invoices will be sent automatically.</span></label>
                </div>
              </div>

              {isAdmin ? <button className="button primary" type="submit">Save automation policy</button> : <p className="read-only-note">This policy is read-only for your role.</p>}
            </form>
          </section>
        </div>

        <aside className="automation-aside">
          <section className="detail-panel policy-summary">
            <h2>Active configuration</h2>
            <dl>
              <div><dt>Mode</dt><dd>{policy.mode}</dd></div>
              <div><dt>Policy version</dt><dd>v{policy.version}</dd></div>
              <div><dt>Last changed</dt><dd>{new Date(policy.createdAt).toLocaleString("en-ZA")}</dd></div>
              <div><dt>Changed by</dt><dd>{policy.createdBy}</dd></div>
              <div><dt>Reason</dt><dd>{policy.reason}</dd></div>
            </dl>
          </section>
          <section className="detail-panel permanent-rules">
            <h2>Permanent safeguards</h2>
            <ul>
              <li>Shadow and Disabled never submit automatically.</li>
              <li>Every send uses the shared idempotent command path.</li>
              <li>Ambiguous outcomes are never retried automatically.</li>
              <li>Current Acumatica source is verified before an automatic send.</li>
            </ul>
          </section>
        </aside>
      </section>

      <section className="table-panel decision-table">
        <div className="table-toolbar"><h2>Recent automation decisions</h2><span>Latest {view.recentDecisions.length}</span></div>
        {view.recentDecisions.length === 0 ? <div className="empty-state"><strong>No decisions recorded</strong><p>Candidate discovery will populate this report.</p></div> : (
          <table>
            <thead><tr><th>Evaluated</th><th>Invoice</th><th>Outcome</th><th>Policy</th><th>Reason</th><th /></tr></thead>
            <tbody>{view.recentDecisions.map((decision) => (
              <tr key={decision.id}>
                <td data-label="Evaluated">{new Date(decision.evaluatedAt).toLocaleString("en-ZA")}</td>
                <td data-label="Invoice"><strong>{decision.invoiceNumber}</strong><span>{decision.invoiceCandidateId}</span></td>
                <td data-label="Outcome"><Status value={decision.outcome} /></td>
                <td data-label="Policy">v{decision.policyVersion}</td>
                <td data-label="Reason">{decision.summary}<span>{decision.reasonCodes.join(", ") || "All checks passed"}</span></td>
                <td className="table-action" data-label="Action"><Link href={`/invoices/${decision.invoiceCandidateId}`}>Open invoice</Link></td>
              </tr>
            ))}</tbody>
          </table>
        )}
      </section>

      <section className="table-panel policy-history">
        <div className="table-toolbar"><h2>Policy history</h2><span>{view.recentVersions.length} versions</span></div>
        <table>
          <thead><tr><th>Version</th><th>Mode</th><th>Emergency stop</th><th>Changed</th><th>Actor</th><th>Reason</th></tr></thead>
          <tbody>{view.recentVersions.map((version) => (
            <tr key={version.version}>
              <td data-label="Version">v{version.version}</td>
              <td data-label="Mode">{version.mode}</td>
              <td data-label="Emergency stop">{version.emergencyStop ? "Active" : "Clear"}</td>
              <td data-label="Changed">{new Date(version.createdAt).toLocaleString("en-ZA")}</td>
              <td data-label="Actor">{version.createdBy}</td>
              <td data-label="Reason">{version.reason}</td>
            </tr>
          ))}</tbody>
        </table>
      </section>
    </main>
  );
}

function NumberField({ label, name, value, suffix, min }: { label: string; name: string; value: number; suffix: string; min: number }) {
  return <label><span>{label}</span><div className="number-field"><input defaultValue={value} min={min} name={name} required type="number" /><small>{suffix}</small></div></label>;
}

function Status({ value }: { value: string }) {
  return <span className={`status-pill status-${value.toLowerCase()}`}>{value}</span>;
}

function timeInput(value: string) {
  return value.slice(0, 5);
}
