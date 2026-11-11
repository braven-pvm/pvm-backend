import type { ReactNode } from "react";
import styles from "./key-value-list.module.css";

export type KeyValue = {
  label: string;
  /** Pass null or undefined for an unknown value; it shows "Not set". */
  value: ReactNode;
  mono?: boolean;
};

/**
 * Labelled facts about one record: the summary on a Record page or the
 * configuration of an automation. Spec: docs/design/design-system.md#keyvaluelist
 */
export function KeyValueList({ items, layout = "columns" }: { items: KeyValue[]; layout?: "columns" | "stacked" }) {
  return (
    <dl className={styles.list} data-layout={layout}>
      {items.map((item) => (
        <div className={styles.row} key={item.label}>
          <dt className={styles.label}>{item.label}</dt>
          <dd className={styles.value} data-mono={item.mono || undefined}>
            {item.value ?? <span className={styles.missing}>Not set</span>}
          </dd>
        </div>
      ))}
    </dl>
  );
}
