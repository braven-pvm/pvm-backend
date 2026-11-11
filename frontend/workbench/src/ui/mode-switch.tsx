"use client";

import { useState } from "react";
import { Button } from "./button";
import { SubmitButton } from "./submit-button";
import styles from "./mode-switch.module.css";

export type AutomationMode = "Off" | "Shadow" | "Live";

const order: AutomationMode[] = ["Off", "Shadow", "Live"];

type ModeSwitchProps = {
  /** The automation's name, used in the label: "Investec feed mode". */
  automation: string;
  current: AutomationMode;
  /** What each mode does for this automation, in one sentence each. */
  consequences: Record<AutomationMode, string>;
  /** A server action. It receives the field "mode". */
  action: (formData: FormData) => void | Promise<void>;
};

/**
 * Off, Shadow or Live for one automation. A change always asks for an inline
 * confirmation that says what the new mode does.
 * Spec: docs/design/design-system.md#modeswitch
 */
export function ModeSwitch({ automation, current, consequences, action }: ModeSwitchProps) {
  const [pending, setPending] = useState<AutomationMode | null>(null);
  // The confirmation stays open with a spinner until the action finishes.
  const confirm = async (formData: FormData) => {
    await action(formData);
    setPending(null);
  };

  return (
    <div className={styles.wrap}>
      <div className={styles.modes} role="group" aria-label={`${automation} mode`}>
        {order.map((mode) => (
          <button
            key={mode}
            type="button"
            className={styles.mode}
            aria-pressed={mode === current}
            data-pending={mode === pending || undefined}
            onClick={() => setPending(mode === current ? null : mode)}
          >
            {mode}
          </button>
        ))}
      </div>
      <p className={styles.current}>{consequences[current]}</p>

      {pending ? (
        <form
          className={styles.confirm}
          action={confirm}
          data-live={pending === "Live" || undefined}
          aria-label={`Confirm the switch to ${pending}`}
        >
          <p className={styles.question}>
            Switch {automation} to {pending}?
          </p>
          <p className={styles.consequence}>{consequences[pending]}</p>
          <input type="hidden" name="mode" value={pending} />
          <div className={styles.actions}>
            <SubmitButton pendingLabel="Switching…">Switch to {pending}</SubmitButton>
            <Button variant="tertiary" onClick={() => setPending(null)}>
              Keep {current}
            </Button>
          </div>
        </form>
      ) : null}
    </div>
  );
}
