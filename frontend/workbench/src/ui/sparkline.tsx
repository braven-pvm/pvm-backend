import styles from "./sparkline.module.css";

type SparklineProps = {
  /** The values in time order, such as the last 12 months. */
  values: number[];
  /** For screen readers: "Sales of the last 12 months, R 1.2 m". */
  label: string;
};

/**
 * A small line of the last months in a table row, with the last value as a dot.
 * It shows the shape only, so the row must give the figures too.
 * Spec: docs/design/design-system.md#sparkline
 */
export function Sparkline({ values, label }: SparklineProps) {
  const width = 96;
  const height = 26;
  const max = Math.max(1, ...values);
  const step = values.length > 1 ? (width - 4) / (values.length - 1) : 0;
  const points = values.map((value, index) => [2 + index * step, height - 3 - (Math.max(0, value) / max) * (height - 6)]);
  if (points.length === 0) {
    return null;
  }
  const line = points.map((point, index) => `${index ? "L" : "M"}${point[0].toFixed(1)} ${point[1].toFixed(1)}`).join(" ");
  const last = points[points.length - 1];
  return (
    <svg className={styles.spark} width={width} height={height} viewBox={`0 0 ${width} ${height}`} role="img" aria-label={label}>
      <path className={styles.area} d={`${line} L${last[0].toFixed(1)} ${height} L2 ${height} Z`} />
      <path className={styles.line} d={line} />
      <circle className={styles.dot} cx={last[0]} cy={last[1]} r={2.5} />
    </svg>
  );
}
