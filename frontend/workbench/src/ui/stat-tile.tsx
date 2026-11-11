import Link from "next/link";
import type { ReactNode } from "react";
import styles from "./stat-tile.module.css";

type StatTileProps = {
  /** What is counted: "Ready to send". */
  label: string;
  /** Already formatted: "51", "R 18 422.40". */
  value: string;
  /** The context that makes the number useful: "0 sent, submission in shadow". */
  detail?: ReactNode;
  /** Opens the list behind the number. */
  href?: string;
  /** warning or error only when the number itself needs attention. */
  tone?: "default" | "warning" | "error";
};

/** One key number. Spec: docs/design/design-system.md#stattile */
export function StatTile({ label, value, detail, href, tone = "default" }: StatTileProps) {
  const content = (
    <>
      <span className={styles.label}>{label}</span>
      <span className={styles.value}>{value}</span>
      {detail ? <span className={styles.detail}>{detail}</span> : null}
    </>
  );
  return href ? (
    <Link className={styles.tile} data-tone={tone} href={href}>
      {content}
    </Link>
  ) : (
    <div className={styles.tile} data-tone={tone}>
      {content}
    </div>
  );
}

/** A responsive row of StatTiles or Tiles. */
export function TileGrid({ children, min = 200 }: { children: ReactNode; min?: number }) {
  return (
    <div className={styles.grid} style={{ gridTemplateColumns: `repeat(auto-fill, minmax(min(${min}px, 100%), 1fr))` }}>
      {children}
    </div>
  );
}
