import Link from "next/link";
import type { ReactNode } from "react";
import styles from "./tile.module.css";

type TileProps = {
  title: string;
  /** Usually a StatusBadge. */
  status?: ReactNode;
  /** One or two short facts: "Live · daily 05:00". */
  meta?: ReactNode;
  href: string;
};

/**
 * A link to one object with its state, such as an automation or a report.
 * Spec: docs/design/design-system.md#tile
 */
export function Tile({ title, status, meta, href }: TileProps) {
  return (
    <Link className={styles.tile} href={href}>
      <span className={styles.title}>{title}</span>
      {status ? <span className={styles.status}>{status}</span> : null}
      {meta ? <span className={styles.meta}>{meta}</span> : null}
    </Link>
  );
}
