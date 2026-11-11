import styles from "./spinner.module.css";

type SpinnerProps = {
  size?: "s" | "m";
  /** Shown next to the spinner. Without it, screen readers hear "Loading". */
  label?: string;
};

/** Spec: docs/design/design-system.md#spinner */
export function Spinner({ size = "m", label }: SpinnerProps) {
  return (
    <span className={styles.wrap} role="status">
      <span className={`${styles.spinner} ${styles[size]}`} aria-hidden="true" />
      {label ? <span className={styles.label}>{label}</span> : <span className="sr-only">Loading</span>}
    </span>
  );
}
