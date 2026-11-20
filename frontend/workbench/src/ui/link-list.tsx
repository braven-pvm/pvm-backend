import Link from "next/link";
import type { ReactNode } from "react";
import styles from "./link-list.module.css";

export type LinkListItem = {
  id: string;
  /** The name of the record: "Valley Distributors". */
  title: string;
  /** One weak line under the name: "Rep H · last order Dec 2025". */
  meta?: string;
  /** Shown at the end of the row, such as a status and a figure. */
  aside?: ReactNode;
  /** The whole row opens this page. */
  href: string;
};

/**
 * A short list of records in a panel, each row a link: the name and one line
 * under it, and a status or a figure at the end. For "Customers to call" and
 * the like on a dashboard. For a list with columns, use DataTable.
 * Spec: docs/design/design-system.md#linklist
 */
export function LinkList({ label, items }: { label: string; items: LinkListItem[] }) {
  return (
    <ul className={styles.list} aria-label={label}>
      {items.map((item) => (
        <li key={item.id}>
          <Link className={styles.row} href={item.href}>
            <span className={styles.text}>
              <span className={styles.title}>{item.title}</span>
              {item.meta ? <span className={styles.meta}>{item.meta}</span> : null}
            </span>
            {item.aside ? <span className={styles.aside}>{item.aside}</span> : null}
          </Link>
        </li>
      ))}
    </ul>
  );
}
