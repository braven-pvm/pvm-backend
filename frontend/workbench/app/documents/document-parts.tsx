import type { InvoiceCandidate } from "../../src/api/documents";
import { formatDocumentState } from "../../src/documents";
import { formatDate } from "../../src/formatters.mjs";
import { StatusBadge, type Column } from "../../src/ui";

/** The state of a document as a badge. A stuck document shows as stuck. */
export function DocumentStateBadge({ state, stuck = false }: { state: string; stuck?: boolean }) {
  return <StatusBadge status={stuck ? "Stuck" : state} label={formatDocumentState(state, stuck)} />;
}

/** Where the PDF stands in Acumatica, for a filed document. */
export function acumaticaLabel(document: { state: string; acumaticaAttachPending: boolean; acumaticaAttachedAt: string | null }) {
  if (document.state !== "Filed") {
    return null;
  }
  if (document.acumaticaAttachedAt) {
    return "Attached";
  }
  return document.acumaticaAttachPending ? "Waiting to attach" : "Not attached";
}

/** The customer of an invoice, with the parent or ship-to when Acumatica has one. */
export function customerOf(candidate: InvoiceCandidate) {
  const name = candidate.customerName ?? candidate.customerId ?? "Unknown customer";
  const extra = candidate.shipToName ?? candidate.parentCustomerName;
  return extra && extra !== name ? `${name} (${extra})` : name;
}

/** The invoice columns shared by the possible invoices and the search results; the page adds the action. */
export const invoiceColumns: Column<InvoiceCandidate>[] = [
  { key: "invoice", header: "Invoice", render: (row) => row.referenceNumber, mono: true },
  { key: "date", header: "Date", render: (row) => (row.date ? formatDate(row.date) : "—"), weak: true },
  { key: "customer", header: "Customer", render: (row) => customerOf(row) },
];
