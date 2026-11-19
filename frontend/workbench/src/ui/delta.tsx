import styles from "./delta.module.css";

type DeltaProps = {
  current: number;
  /** The value to compare with, such as the same months last year. */
  previous: number;
};

/**
 * The change of a figure as "▲ 12.3%", green up, red down. With nothing to compare
 * with, it says "New". The arrow carries the direction, so it reads without colour.
 * Spec: docs/design/design-system.md#delta
 */
export function Delta({ current, previous }: DeltaProps) {
  if (previous <= 0) {
    return current > 0 ? (
      <span className={styles.delta} data-tone="up">
        New
      </span>
    ) : (
      <span className={styles.delta} data-tone="flat">
        –
      </span>
    );
  }
  const percent = ((current - previous) / previous) * 100;
  const tone = percent > 1 ? "up" : percent < -1 ? "down" : "flat";
  const arrow = percent > 0 ? "▲" : percent < 0 ? "▼" : "";
  return (
    <span className={styles.delta} data-tone={tone}>
      <span aria-hidden="true">{arrow}</span>
      <span className="sr-only">{percent > 0 ? "Up " : percent < 0 ? "Down " : ""}</span>
      {Math.abs(percent).toFixed(1)}%
    </span>
  );
}
