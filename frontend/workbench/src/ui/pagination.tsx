import Link from "next/link";
import { ChevronLeft, ChevronRight } from "lucide-react";
import styles from "./pagination.module.css";

type PaginationProps = {
  /** The first and last record on this page, counted from 1. */
  from: number;
  to: number;
  /** Leave unset when the API does not return a total. */
  total?: number;
  previousHref?: string;
  nextHref?: string;
};

/** Spec: docs/design/design-system.md#pagination */
export function Pagination({ from, to, total, previousHref, nextHref }: PaginationProps) {
  return (
    <nav className={styles.bar} aria-label="Pages">
      <p className={styles.range}>
        {from}–{to}
        {total !== undefined ? ` of ${total}` : null}
      </p>
      <div className={styles.links}>
        {previousHref ? (
          <Link className={styles.link} href={previousHref}>
            <ChevronLeft size={16} aria-hidden="true" />
            Previous page
          </Link>
        ) : null}
        {nextHref ? (
          <Link className={styles.link} href={nextHref}>
            Next page
            <ChevronRight size={16} aria-hidden="true" />
          </Link>
        ) : null}
      </div>
    </nav>
  );
}
