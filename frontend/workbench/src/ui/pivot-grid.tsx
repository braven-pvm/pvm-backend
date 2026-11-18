import Link from "next/link";
import { ChevronDown, ChevronRight } from "lucide-react";
import type { ReactNode } from "react";
import styles from "./pivot-grid.module.css";

export type PivotRow = {
  /** A stable key, such as the path "R1|RETAIL". */
  key: string;
  /** 0 for the top level. Each level is indented. */
  level: number;
  label: string;
  /** Already formatted, one for each header. */
  values: string[];
  total: string;
  /** The URL that opens or closes the row. Leave it out for a row with no rows under it. */
  toggleHref?: string;
  open?: boolean;
};

type PivotGridProps = {
  /** Read by screen readers: "Sales by rep". */
  caption: string;
  /** The header of the first column, which names the levels: "Rep, customer group, customer". */
  rowHeader: string;
  /** An optional first header row that groups the columns, such as the financial years. */
  groups?: { label: string; span: number }[];
  headers: string[];
  /** The indexes of the columns that are subtotals, such as the year totals. They show in bold. */
  subtotalColumns?: number[];
  rows: PivotRow[];
  totals?: { label?: string; values: string[]; total: string };
  /** Shown instead of the grid when there are no rows. Use EmptyState. */
  empty: ReactNode;
};

/**
 * A report grid: rows in levels that open and close, many figure columns, a
 * total column and a totals row. The first column and the headers stay in view
 * when the grid scrolls. A row opens with a link, so that the open rows are in
 * the URL and the grid works without JavaScript.
 * Spec: docs/design/design-system.md#pivotgrid
 */
export function PivotGrid({ caption, rowHeader, groups, headers, subtotalColumns = [], rows, totals, empty }: PivotGridProps) {
  if (rows.length === 0) {
    return <>{empty}</>;
  }

  const subtotal = new Set(subtotalColumns);
  return (
    <div className={styles.scroll} tabIndex={0} role="region" aria-label={caption}>
      <table className={styles.table}>
        <caption className="sr-only">{caption}</caption>
        <thead>
          {groups ? (
            <tr>
              <th className={`${styles.th} ${styles.rowHead}`} scope="col" rowSpan={2}>
                {rowHeader}
              </th>
              {groups.map((group) => (
                <th key={group.label} className={`${styles.th} ${styles.group}`} scope="colgroup" colSpan={group.span}>
                  {group.label}
                </th>
              ))}
              <th className={`${styles.th} ${styles.num}`} scope="col" rowSpan={2}>
                Total
              </th>
            </tr>
          ) : null}
          <tr>
            {groups ? null : (
              <th className={`${styles.th} ${styles.rowHead}`} scope="col">
                {rowHeader}
              </th>
            )}
            {headers.map((header, index) => (
              <th key={`${header}-${index}`} className={`${styles.th} ${styles.num}`} data-subtotal={subtotal.has(index) || undefined} scope="col">
                {header}
              </th>
            ))}
            {groups ? null : (
              <th className={`${styles.th} ${styles.num}`} scope="col">
                Total
              </th>
            )}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.key} data-level={row.level}>
              <th className={`${styles.td} ${styles.rowHead}`} scope="row">
                <span className={styles.label} style={{ paddingInlineStart: `calc(${row.level} * var(--space-m))` }}>
                  {row.toggleHref ? (
                    <Link className={styles.toggle} href={row.toggleHref} scroll={false} aria-expanded={row.open ?? false}>
                      {row.open ? <ChevronDown size={16} aria-hidden="true" /> : <ChevronRight size={16} aria-hidden="true" />}
                      <span className="sr-only">{row.open ? "Close " : "Open "}</span>
                      {row.label}
                    </Link>
                  ) : (
                    <span className={styles.leaf}>{row.label}</span>
                  )}
                </span>
              </th>
              {row.values.map((value, index) => (
                <td key={index} className={`${styles.td} ${styles.num}`} data-subtotal={subtotal.has(index) || undefined}>
                  {value}
                </td>
              ))}
              <td className={`${styles.td} ${styles.num} ${styles.total}`}>{row.total}</td>
            </tr>
          ))}
        </tbody>
        {totals ? (
          <tfoot>
            <tr>
              <th className={`${styles.td} ${styles.rowHead} ${styles.foot}`} scope="row">
                {totals.label ?? "Total"}
              </th>
              {totals.values.map((value, index) => (
                <td key={index} className={`${styles.td} ${styles.num} ${styles.foot}`}>
                  {value}
                </td>
              ))}
              <td className={`${styles.td} ${styles.num} ${styles.foot}`}>{totals.total}</td>
            </tr>
          </tfoot>
        ) : null}
      </table>
    </div>
  );
}
