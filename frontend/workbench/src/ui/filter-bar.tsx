import Link from "next/link";
import type { ReactNode } from "react";
import { Button } from "./button";
import styles from "./filter-bar.module.css";

type FilterBarProps = {
  /** The list's path. The bar submits a GET form, so the filters go in the URL. */
  action: string;
  /** Read by screen readers: "Filter invoices". */
  label: string;
  /** URL parameters to keep, such as the current tab or sort. */
  keep?: Record<string, string>;
  /** The number of filters in use. With 1 or more, "Clear filters" shows. */
  activeCount?: number;
  /** Fields with showOptional={false}: TextField type="search", SelectField, TextField type="date". */
  children: ReactNode;
};

/**
 * Filters for a list with more than one value to filter by. It works without
 * JavaScript: a GET form, an "Apply filters" button, a "Clear filters" link.
 * For one value with few options, use FilterChips.
 * Spec: docs/design/design-system.md#filterbar
 */
export function FilterBar({ action, label, keep = {}, activeCount = 0, children }: FilterBarProps) {
  return (
    <form className={styles.bar} method="get" action={action} role="search" aria-label={label}>
      {Object.entries(keep).map(([name, value]) => (
        <input key={name} type="hidden" name={name} value={value} />
      ))}
      <div className={styles.fields}>{children}</div>
      <div className={styles.actions}>
        <Button type="submit" variant="secondary">
          Apply filters
        </Button>
        {activeCount > 0 ? (
          <Link className={styles.clear} href={action}>
            Clear filters ({activeCount})
          </Link>
        ) : null}
      </div>
    </form>
  );
}
