import type { ReactNode } from "react";
import styles from "./compact.module.css";

/**
 * The dense size of the report pages: 14 px text, 26 px headings and figures,
 * 32 px controls and less space. It sets the size tokens for everything inside
 * it, so the components shrink with no change of their own. Use it only for
 * report pages that show many figures at once, such as the sales report.
 * Spec: docs/design/design-system.md#compact
 */
export function Compact({ children }: { children: ReactNode }) {
  return <div className={styles.compact}>{children}</div>;
}
