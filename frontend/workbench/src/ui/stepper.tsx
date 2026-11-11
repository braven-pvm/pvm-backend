import { Check } from "lucide-react";
import styles from "./stepper.module.css";

type StepperProps = {
  /** Short step names: "Choose file", "Check what will change", "Done". */
  steps: string[];
  /** The current step, counted from 0. */
  current: number;
};

/**
 * Where the user is in a Task page of 3 to 5 steps.
 * Spec: docs/design/design-system.md#stepper
 */
export function Stepper({ steps, current }: StepperProps) {
  return (
    <ol className={styles.steps} aria-label="Steps">
      {steps.map((step, index) => {
        const state = index < current ? "done" : index === current ? "current" : "todo";
        return (
          <li
            key={step}
            className={styles.step}
            data-state={state}
            aria-current={state === "current" ? "step" : undefined}
          >
            <span className={styles.dot} aria-hidden="true">
              {state === "done" ? <Check size={14} /> : index + 1}
            </span>
            <span>
              {step}
              {state === "done" ? <span className="sr-only"> (done)</span> : null}
            </span>
          </li>
        );
      })}
    </ol>
  );
}
