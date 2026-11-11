import type { ReactNode } from "react";
import styles from "./timeline.module.css";

export type TimelineEntry = {
  id: string;
  /** Already formatted: "11 Nov, 05:00". */
  time: string;
  /** An ISO timestamp for the <time> element. */
  dateTime?: string;
  /** What happened, in the past tense: "Invoice finalised in Acumatica". */
  title: ReactNode;
  detail?: ReactNode;
  /** Usually a StatusBadge. */
  status?: ReactNode;
};

/**
 * What happened to one record or automation, newest first.
 * Spec: docs/design/design-system.md#timeline
 */
export function Timeline({ entries, label }: { entries: TimelineEntry[]; label: string }) {
  return (
    <ol className={styles.list} aria-label={label}>
      {entries.map((entry) => (
        <li className={styles.entry} key={entry.id}>
          <time className={styles.time} dateTime={entry.dateTime}>
            {entry.time}
          </time>
          <div className={styles.text}>
            <div className={styles.title}>
              {entry.status}
              <span>{entry.title}</span>
            </div>
            {entry.detail ? <div className={styles.detail}>{entry.detail}</div> : null}
          </div>
        </li>
      ))}
    </ol>
  );
}
