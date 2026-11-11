import type { ReactNode } from "react";
import { StatusBadge, toneFor, type StatusTone } from "./status-badge";
import styles from "./attention-list.module.css";

export type AttentionItem = {
  id: string;
  /** The status word for the badge: "Failed", "Needs review", "Overdue". */
  status: string;
  tone?: StatusTone;
  /** What is wrong, with the object named: "Investec feed failed at 05:00". */
  title: string;
  /** The consequence or the fix, in one sentence. */
  detail?: string;
  /** One action that leads to the fix. Usually a secondary ButtonLink. */
  action?: ReactNode;
};

const severity: Record<StatusTone, number> = { error: 0, warning: 1, info: 2, neutral: 3, success: 4 };

/**
 * Items that need a person, most severe first. The list sorts itself.
 * Spec: docs/design/design-system.md#attentionlist
 */
export function AttentionList({ items, label }: { items: AttentionItem[]; label: string }) {
  const sorted = [...items].sort(
    (a, b) => severity[a.tone ?? toneFor(a.status)] - severity[b.tone ?? toneFor(b.status)],
  );
  return (
    <ul className={styles.list} aria-label={label}>
      {sorted.map((item) => (
        <li className={styles.item} key={item.id}>
          <span className={styles.badge}>
            <StatusBadge status={item.status} tone={item.tone} />
          </span>
          <div className={styles.text}>
            <p className={styles.title}>{item.title}</p>
            {item.detail ? <p className={styles.detail}>{item.detail}</p> : null}
          </div>
          {item.action ? <div className={styles.action}>{item.action}</div> : null}
        </li>
      ))}
    </ul>
  );
}
