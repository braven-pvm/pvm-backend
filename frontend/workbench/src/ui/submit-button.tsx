"use client";

import type { ReactNode } from "react";
import { useFormStatus } from "react-dom";
import { buttonClass, type ButtonVariant } from "./button";
import { Spinner } from "./spinner";

type SubmitButtonProps = {
  variant?: ButtonVariant;
  /** The label while the form is sending, such as "Importing…". */
  pendingLabel: string;
  children: ReactNode;
};

/**
 * The submit button of a form. While the form sends, it shows a spinner and
 * the pending label, and a second press does nothing.
 * Spec: docs/design/design-system.md#submitbutton
 */
export function SubmitButton({ variant = "primary", pendingLabel, children }: SubmitButtonProps) {
  const { pending } = useFormStatus();
  return (
    <button
      type="submit"
      className={buttonClass(variant)}
      aria-disabled={pending}
      onClick={(event) => {
        if (pending) {
          event.preventDefault();
        }
      }}
    >
      {pending ? (
        <>
          <Spinner size="s" />
          {pendingLabel}
        </>
      ) : (
        children
      )}
    </button>
  );
}
