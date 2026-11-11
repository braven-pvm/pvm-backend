import type { ExceptionQueueSummary } from "../../src/api/client";

export const queues = [
  { key: "all", label: "All open", category: undefined, status: "active", count: (s: ExceptionQueueSummary) => s.ambiguous + s.rejected + s.needsReview + s.deadLetters + s.stuck + s.held },
  { key: "ambiguous", label: "Ambiguous", category: "AmbiguousSubmission", status: "active", count: (s: ExceptionQueueSummary) => s.ambiguous },
  { key: "rejected", label: "Rejected", category: "RejectedSubmission", status: "active", count: (s: ExceptionQueueSummary) => s.rejected },
  { key: "needs-review", label: "Needs review", category: "NeedsReview", status: "active", count: (s: ExceptionQueueSummary) => s.needsReview },
  { key: "dead-letters", label: "Dead letters", category: "DeadLetter", status: "active", count: (s: ExceptionQueueSummary) => s.deadLetters },
  { key: "stuck", label: "Stuck", category: "StuckWork", status: "active", count: (s: ExceptionQueueSummary) => s.stuck },
  { key: "held", label: "Held", category: "HeldInvoice", status: "active", count: (s: ExceptionQueueSummary) => s.held },
  { key: "resolved", label: "Resolved", category: undefined, status: "Resolved", count: (s: ExceptionQueueSummary) => s.resolved },
] as const;

/** "AmbiguousSubmission" -> "Ambiguous submission". */
export function categoryLabel(category: string) {
  const words = category.replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

export const severityTone = (severity: string) =>
  severity.toLowerCase() === "high" || severity.toLowerCase() === "critical"
    ? ("error" as const)
    : severity.toLowerCase() === "medium"
      ? ("warning" as const)
      : ("neutral" as const);
