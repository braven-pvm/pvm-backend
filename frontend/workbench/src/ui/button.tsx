import Link from "next/link";
import type { ButtonHTMLAttributes, ReactNode } from "react";
import styles from "./button.module.css";

/**
 * primary: the single most important action on the screen (one per screen).
 * secondary: other actions. tertiary: low-priority and destructive actions.
 * Labels are a verb and a noun: "Import statement", "Run now".
 */
export type ButtonVariant = "primary" | "secondary" | "tertiary";

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: ButtonVariant;
};

export function Button({ variant = "secondary", className, type = "button", ...props }: ButtonProps) {
  return (
    <button
      type={type}
      className={[styles.button, styles[variant], className].filter(Boolean).join(" ")}
      {...props}
    />
  );
}

type ButtonLinkProps = {
  href: string;
  variant?: ButtonVariant;
  children: ReactNode;
};

export function ButtonLink({ href, variant = "secondary", children }: ButtonLinkProps) {
  return (
    <Link href={href} className={`${styles.button} ${styles[variant]}`}>
      {children}
    </Link>
  );
}
