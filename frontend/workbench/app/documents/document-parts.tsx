import type { InvoiceCandidate } from "../../src/api/documents";
import { acumaticaAttachLabel, acumaticaInvoiceUrl, formatDocumentState } from "../../src/documents";
import { formatDate } from "../../src/formatters.mjs";
import { StatusBadge, type Column } from "../../src/ui";

/** The state of a document as a badge. A stuck document shows as stuck. */
export function DocumentStateBadge({ state, stuck = false }: { state: string; stuck?: boolean }) {
  return <StatusBadge status={stuck ? "Stuck" : state} label={formatDocumentState(state, stuck)} />;
}

/**
 * The invoice number, as a link to the invoice in Acumatica when the
 * ACUMATICA_BASE_URL setting is there, otherwise as plain text.
 */
export function AcumaticaInvoice({ reference, baseUrl }: { reference: string; baseUrl: string | undefined }) {
  const href = acumaticaInvoiceUrl(baseUrl, reference);
  if (!href) {
    return <>{reference}</>;
  }
  return (
    <a href={href} target="_blank" rel="noopener noreferrer">
      {reference}
      <span className="sr-only"> (opens Acumatica in a new tab)</span>
    </a>
  );
}

/**
 * "Linked to INV190096" for a filed document, the invoice a link to Acumatica
 * when the setting is there. The attach state follows only in Write mode
 * (design 6.13). Null when the document is not filed to an invoice.
 */
export function InvoiceLink({
  document,
  mode,
  baseUrl,
}: {
  document: { state: string; matchedReference: string | null; acumaticaAttachPending: boolean; acumaticaAttachedAt: string | null };
  mode: string | null | undefined;
  baseUrl: string | undefined;
}) {
  if (document.state !== "Filed" || !document.matchedReference) {
    return null;
  }
  const attach = acumaticaAttachLabel(document, mode);
  return (
    <>
      Linked to <AcumaticaInvoice reference={document.matchedReference} baseUrl={baseUrl} />
      {attach ? ` · ${attach}` : null}
    </>
  );
}

/** The customer of an invoice, with the parent or ship-to when Acumatica has one. */
export function customerOf(candidate: InvoiceCandidate) {
  const name = candidate.customerName ?? candidate.customerId ?? "Unknown customer";
  const extra = candidate.shipToName ?? candidate.parentCustomerName;
  return extra && extra !== name ? `${name} (${extra})` : name;
}

/**
 * The invoice columns shared by the possible invoices and the search results;
 * the page adds the action. One column, so it fits the document page's narrow
 * side panel with the File to this invoice button next to it.
 */
export const invoiceColumns: Column<InvoiceCandidate>[] = [
  {
    key: "invoice",
    header: "Invoice",
    render: (row) => (
      <>
        {row.referenceNumber}
        <br />
        {customerOf(row)}
        {row.date ? `, ${formatDate(row.date)}` : null}
      </>
    ),
  },
];
