import Link from "next/link";
import type { ButtonHTMLAttributes, ReactNode } from "react";
import styles from "./button.module.css";

/**
 * primary: the single most important action on the screen (one per screen).
 * secondary: other actions. tertiary: low-priority and destructive triggers.
 * danger: only the confirm button inside a ConfirmDialog for a destructive action.
 * Labels are a verb and a noun: "Import statement", "Run now".
 * Spec: docs/design/design-system.md#button
 */
export type ButtonVariant = "primary" | "secondary" | "tertiary" | "danger";

export function buttonClass(variant: ButtonVariant, className?: string) {
  return [styles.button, styles[variant], className].filter(Boolean).join(" ");
}

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: ButtonVariant;
};

export function Button({ variant = "secondary", className, type = "button", ...props }: ButtonProps) {
  return <button type={type} className={buttonClass(variant, className)} {...props} />;
}

type ButtonLinkProps = {
  href: string;
  variant?: ButtonVariant;
  children: ReactNode;
};

/** A link that looks like a button. Use it when the action opens another page. */
export function ButtonLink({ href, variant = "secondary", children }: ButtonLinkProps) {
  return (
    <Link href={href} className={buttonClass(variant)}>
      {children}
    </Link>
  );
}
