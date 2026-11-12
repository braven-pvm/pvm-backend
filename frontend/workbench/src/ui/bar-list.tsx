import styles from "./bar-list.module.css";

export type Bar = {
  label: string;
  value: number;
  /** Already formatted, shown at the end of the row: "29 / 30". */
  display: string;
  /** Mark a bar that is below its target, so it reads without colour too. */
  low?: boolean;
};

type BarListProps = {
  /** What the bars measure, for screen readers: "Days covered in the last 30 days". */
  label: string;
  bars: Bar[];
  max: number;
  /** Text under the bars: "0", "15", "30 days". Three values at most. */
  axis?: string[];
};

/**
 * Horizontal bars that compare one measure across a few items (12 at most).
 * The numbers are always printed, so the chart needs no tooltip.
 * Spec: docs/design/design-system.md#barlist
 */
export function BarList({ label, bars, max, axis }: BarListProps) {
  return (
    <figure className={styles.figure}>
      <figcaption className="sr-only">{label}</figcaption>
      <ul className={styles.list}>
        {bars.map((bar) => (
          <li className={styles.row} key={bar.label}>
            <span className={styles.label}>{bar.label}</span>
            <span className={styles.track} aria-hidden="true">
              <span
                className={styles.fill}
                data-low={bar.low || undefined}
                style={{ width: `${Math.max(0, Math.min(100, (bar.value / max) * 100))}%` }}
              />
            </span>
            <span className={styles.value}>
              {bar.display}
              {bar.low ? <span className={styles.lowMark}> Low</span> : null}
            </span>
          </li>
        ))}
      </ul>
      {axis ? (
        <div className={styles.axis} aria-hidden="true">
          <span />
          <span className={styles.ticks}>
            {axis.map((tick) => (
              <span key={tick}>{tick}</span>
            ))}
          </span>
          <span />
        </div>
      ) : null}
    </figure>
  );
}
