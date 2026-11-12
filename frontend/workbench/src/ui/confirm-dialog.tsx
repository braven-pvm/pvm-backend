"use client";

import { useId, useRef, type ReactNode } from "react";
import { X } from "lucide-react";
import { Button, type ButtonVariant } from "./button";
import { SubmitButton } from "./submit-button";
import styles from "./confirm-dialog.module.css";

type ConfirmDialogProps = {
  /** The button that opens the dialog: "Delete mapping". */
  triggerLabel: string;
  /** tertiary for destructive actions, so the first press is not tempting. */
  triggerVariant?: ButtonVariant;
  /** A question that names the action and the object: "Delete the mapping for ENER12?" */
  title: string;
  /** The consequence, in plain words. Say if it cannot be undone. */
  children: ReactNode;
  /** Repeats the action: "Delete mapping". Never "OK" or "Yes". */
  confirmLabel: string;
  pendingLabel: string;
  /** danger makes the confirm button red. Use it only when data is lost. */
  tone?: "default" | "danger";
  /** A server action. It receives the hidden fields. */
  action: (formData: FormData) => void | Promise<void>;
  fields?: Record<string, string>;
};

/**
 * A modal confirmation before an action that is hard to undo or reaches
 * production. Uses the native <dialog>: focus moves in, Esc closes, focus
 * returns to the trigger. Spec: docs/design/design-system.md#confirmdialog
 */
export function ConfirmDialog({
  triggerLabel,
  triggerVariant = "secondary",
  title,
  children,
  confirmLabel,
  pendingLabel,
  tone = "default",
  action,
  fields = {},
}: ConfirmDialogProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  const close = () => dialogRef.current?.close();
  // The dialog stays open with a spinner until the action finishes.
  const confirm = async (formData: FormData) => {
    await action(formData);
    close();
  };

  return (
    <>
      <Button variant={triggerVariant} onClick={() => dialogRef.current?.showModal()}>
        {triggerLabel}
      </Button>
      <dialog
        ref={dialogRef}
        className={styles.dialog}
        aria-labelledby={titleId}
        onClick={(event) => {
          // A press on the backdrop lands on the dialog element itself.
          if (event.target === dialogRef.current) {
            close();
          }
        }}
      >
        <form action={confirm} className={styles.form}>
          <div className={styles.head}>
            <h2 className={styles.title} id={titleId}>
              {title}
            </h2>
            <button type="button" className={styles.close} onClick={close} aria-label="Close">
              <X size={18} aria-hidden="true" />
            </button>
          </div>
          <div className={styles.body}>{children}</div>
          {Object.entries(fields).map(([name, value]) => (
            <input key={name} type="hidden" name={name} value={value} />
          ))}
          <div className={styles.actions}>
            <SubmitButton variant={tone === "danger" ? "danger" : "primary"} pendingLabel={pendingLabel}>
              {confirmLabel}
            </SubmitButton>
            <Button variant="tertiary" onClick={close}>
              Cancel
            </Button>
          </div>
        </form>
      </dialog>
    </>
  );
}
