"use client";

import { resolveDeadLettersAction } from "../actions";

const queues = [
  { value: "", label: "Every queue" },
  { value: "shoprite-po-refresh", label: "shoprite-po-refresh" },
  { value: "acumatica-invoice-discovery", label: "acumatica-invoice-discovery" },
  { value: "shoprite-invoice-submit", label: "shoprite-invoice-submit" },
];

export function DeadLetterBulkForm({ openCount }: { openCount: number }) {
  return (
    <form
      action={resolveDeadLettersAction}
      className="exception-form bulk-form"
      onSubmit={(event) => {
        if (!window.confirm(
          "Resolve every matching dead letter? The messages are not replayed, and the reason is audited.",
        )) {
          event.preventDefault();
        }
      }}
    >
      <fieldset>
        <legend>Close historical dead letters</legend>
        <p className="field-note">
          Use this when an incident is already fixed and the broker no longer holds the messages.
          Resolution never replays a message. {openCount} dead-letter task(s) are open.
        </p>
        <label>
          <span>Queue</span>
          <select name="queueName" defaultValue="">
            {queues.map((queue) => (
              <option key={queue.value} value={queue.value}>{queue.label}</option>
            ))}
          </select>
        </label>
        <label>
          <span>Older than (days) *</span>
          <input name="olderThanDays" type="number" min={0} defaultValue={1} required />
        </label>
        <label>
          <span>Reason *</span>
          <input name="reason" placeholder="Incident reference and why replay is unnecessary" required />
        </label>
        <button className="button secondary" type="submit">Resolve matching dead letters</button>
      </fieldset>
    </form>
  );
}
