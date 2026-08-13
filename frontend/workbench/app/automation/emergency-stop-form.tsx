"use client";

import { setAutomationEmergencyStopAction } from "../actions";

export function EmergencyStopForm({ active, policyVersion }: { active: boolean; policyVersion: number }) {
  return (
    <form
      action={setAutomationEmergencyStopAction}
      className="emergency-form"
      onSubmit={(event) => {
        const consequence = active
          ? "Clear the emergency stop and permit new submissions according to the current policy?"
          : "Activate the emergency stop and block all new manual and automatic submissions?";
        if (!window.confirm(consequence)) event.preventDefault();
      }}
    >
      <input name="expectedVersion" type="hidden" value={policyVersion} />
      <input name="active" type="hidden" value={active ? "false" : "true"} />
      <label>
        <span>Reason *</span>
        <input name="reason" placeholder="Incident, test or clearance reference" required />
      </label>
      <button className={`button ${active ? "primary" : "secondary"}`} type="submit">
        {active ? "Clear emergency stop" : "Activate emergency stop"}
      </button>
    </form>
  );
}
