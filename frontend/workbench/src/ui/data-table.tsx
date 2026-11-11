import Link from "next/link";
import { ArrowDown, ArrowUp, ArrowUpDown } from "lucide-react";
import type { ReactNode } from "react";
import styles from "./data-table.module.css";

export type SortDirection = "asc" | "desc";

export type Column<Row> = {
  key: string;
  header: string;
  render: (row: Row) => ReactNode;
  /** "end" for numbers and money, so digits line up. */
  align?: "start" | "end";
  /** Identifiers: invoice numbers, GLNs, cash accounts. */
  mono?: boolean;
  /** Secondary information in weak text. */
  weak?: boolean;
  sortable?: boolean;
};

type DataTableProps<Row> = {
  /** Read by screen readers only: "Shoprite invoices". */
  caption: string;
  columns: Column<Row>[];
  rows: Row[];
  rowKey: (row: Row) => string;
  /** The record page of a row. The first cell becomes the link and the whole row is clickable. */
  rowHref?: (row: Row) => string;
  sort?: { key: string; direction: SortDirection };
  /** Builds the URL for a sort, so sorting is server-side and shareable. */
  sortHref?: (key: string, direction: SortDirection) => string;
  /** Shown instead of the table when there are no rows. Use EmptyState. */
  empty: ReactNode;
};

/**
 * A table of records. Sorting, filtering and paging live in the URL, and the
 * server returns the rows. Spec: docs/design/design-system.md#datatable
 */
export function DataTable<Row>({
  caption,
  columns,
  rows,
  rowKey,
  rowHref,
  sort,
  sortHref,
  empty,
}: DataTableProps<Row>) {
  if (rows.length === 0) {
    return <>{empty}</>;
  }

  return (
    <div className={styles.scroll}>
      <table className={styles.table}>
        <caption className="sr-only">{caption}</caption>
        <thead>
          <tr>
            {columns.map((column) => (
              <th
                key={column.key}
                scope="col"
                className={styles.th}
                data-align={column.align}
                aria-sort={
                  sort?.key === column.key
                    ? sort.direction === "asc"
                      ? "ascending"
                      : "descending"
                    : undefined
                }
              >
                {column.sortable && sortHref ? (
                  <SortLink column={column} sort={sort} sortHref={sortHref} />
                ) : (
                  column.header
                )}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => {
            const href = rowHref?.(row);
            return (
              <tr key={rowKey(row)} className={href ? styles.linkRow : undefined}>
                {columns.map((column, index) => {
                  const content = column.render(row);
                  return (
                    <td
                      key={column.key}
                      className={styles.td}
                      data-align={column.align}
                      data-mono={column.mono || undefined}
                      data-weak={column.weak || undefined}
                    >
                      {href && index === 0 ? (
                        <Link className={styles.rowLink} href={href}>
                          {content}
                        </Link>
                      ) : (
                        content
                      )}
                    </td>
                  );
                })}
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function SortLink<Row>({
  column,
  sort,
  sortHref,
}: {
  column: Column<Row>;
  sort?: { key: string; direction: SortDirection };
  sortHref: (key: string, direction: SortDirection) => string;
}) {
  const active = sort?.key === column.key;
  const next: SortDirection = active && sort?.direction === "asc" ? "desc" : "asc";
  const Icon = !active ? ArrowUpDown : sort?.direction === "asc" ? ArrowUp : ArrowDown;
  return (
    <Link className={styles.sortLink} href={sortHref(column.key, next)}>
      {column.header}
      <Icon size={14} aria-hidden="true" />
    </Link>
  );
}
