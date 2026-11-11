import { CircleAlert, CircleCheck, CircleX, Info, type LucideIcon } from "lucide-react";
import type { ReactNode } from "react";
import styles from "./notice.module.css";

export type NoticeTone = "info" | "success" | "warning" | "error";

const icons: Record<NoticeTone, LucideIcon> = {
  info: Info,
  success: CircleCheck,
  warning: CircleAlert,
  error: CircleX,
};

type NoticeProps = {
  tone: NoticeTone;
  /** What happened, in one short sentence: "Statement 001319 created". */
  title: string;
  /** What it means or what to do next. */
  children?: ReactNode;
  /** One action, usually a ButtonLink or a tertiary Button. */
  action?: ReactNode;
  /**
   * Set when the notice appears as the result of something the user did, so
   * screen readers announce it. Leave unset for notices present at page load.
   */
  live?: boolean;
};

/**
 * A message about the page or the result of an action. It replaces toasts:
 * results stay on the page where the user acted.
 * Spec: docs/design/design-system.md#notice
 */
export function Notice({ tone, title, children, action, live = false }: NoticeProps) {
  const Icon = icons[tone];
  const role = live ? (tone === "error" ? "alert" : "status") : undefined;
  return (
    <div className={`${styles.notice} ${styles[tone]}`} role={role}>
      <Icon className={styles.icon} size={18} aria-hidden="true" />
      <div className={styles.text}>
        <p className={styles.title}>{title}</p>
        {children ? <div className={styles.body}>{children}</div> : null}
      </div>
      {action ? <div className={styles.action}>{action}</div> : null}
    </div>
  );
}
