import Link from "next/link";
import styles from "./filter-chips.module.css";

export type FilterChip = {
  label: string;
  /** The number of records the filter shows, when the API returns it. */
  count?: number;
  href: string;
  current: boolean;
};

type FilterChipsProps = {
  /** Read by screen readers: "Filter invoices by status". */
  label: string;
  chips: FilterChip[];
};

/**
 * One-choice quick filters above a list. Each chip is a link that sets a URL
 * parameter, so a filtered list can be shared and reloaded.
 * Spec: docs/design/design-system.md#filterchips
 */
export function FilterChips({ label, chips }: FilterChipsProps) {
  return (
    <nav aria-label={label}>
      <ul className={styles.list}>
        {chips.map((chip) => (
          <li key={chip.href}>
            <Link className={styles.chip} href={chip.href} aria-current={chip.current ? "true" : undefined}>
              {chip.label}
              {chip.count !== undefined ? <span className={styles.count}>{chip.count}</span> : null}
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}
