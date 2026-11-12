import { CircleAlert, CircleCheck, CircleDashed, CircleX, Info, type LucideIcon } from "lucide-react";
import styles from "./status-badge.module.css";

export type StatusTone = "success" | "error" | "warning" | "info" | "neutral";

const toneIcons: Record<StatusTone, LucideIcon> = {
  success: CircleCheck,
  error: CircleX,
  warning: CircleAlert,
  info: Info,
  neutral: CircleDashed,
};

// Keys are status values with case, spaces, hyphens and underscores removed.
const toneByStatus: Record<string, StatusTone> = {
  succeeded: "success",
  success: "success",
  completed: "success",
  complete: "success",
  healthy: "success",
  ready: "success",
  submitted: "success",
  accepted: "success",
  active: "success",
  live: "success",
  done: "success",
  processed: "success",
  resolved: "success",
  failed: "error",
  failure: "error",
  error: "error",
  rejected: "error",
  blocked: "error",
  failing: "error",
  deadlettered: "error",
  deadletter: "error",
  disabled: "neutral",
  off: "neutral",
  needsreview: "warning",
  review: "warning",
  stale: "warning",
  late: "warning",
  warning: "warning",
  ambiguous: "warning",
  open: "warning",
  shadow: "info",
  running: "info",
  inprogress: "info",
  stabilizing: "info",
  fresh: "success",
};

export function toneFor(status: string): StatusTone {
  return toneByStatus[status.toLowerCase().replace(/[\s_-]/g, "")] ?? "neutral";
}

/** "NeedsReview" or "needs_review" -> "Needs review". */
export function statusLabel(status: string) {
  const words = status
    .replace(/[_-]+/g, " ")
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .trim()
    .toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

type StatusBadgeProps = {
  status: string;
  /** Override the tone derived from the status. */
  tone?: StatusTone;
  /** Override the label derived from the status. */
  label?: string;
};

/** A status as icon and word, so meaning never depends on colour alone. */
export function StatusBadge({ status, tone, label }: StatusBadgeProps) {
  const resolved = tone ?? toneFor(status);
  const Icon = toneIcons[resolved];
  return (
    <span className={`${styles.badge} ${styles[resolved]}`}>
      <Icon size={14} aria-hidden="true" />
      {label ?? statusLabel(status)}
    </span>
  );
}
