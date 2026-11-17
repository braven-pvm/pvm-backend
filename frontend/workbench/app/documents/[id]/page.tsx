import { notFound } from "next/navigation";
import type { ReactNode } from "react";
import { getDocument, searchReviewInvoices, type DocumentDetail, type InvoiceCandidate } from "../../../src/api/documents";
import { hasAnyRole, requireWorkbenchUser } from "../../../src/auth/session";
import {
  canActOnDocument,
  documentTypeLabel,
  formatDocumentState,
  formatReviewReason,
  readExtraction,
} from "../../../src/documents";
import { formatDate, formatDateTime } from "../../../src/formatters.mjs";
import {
  Button,
  Columns,
  ConfirmDialog,
  DataTable,
  EmptyState,
  KeyValueList,
  Notice,
  Panel,
  PdfFrame,
  RecordPage,
  Stack,
  TextareaField,
  TextField,
  Timeline,
  type Column,
  type TimelineEntry,
} from "../../../src/ui";
import { fileDocumentToAction, rejectDocumentAction } from "../actions";
import { DocumentStateBadge, acumaticaLabel, customerOf, invoiceColumns } from "../document-parts";

export const dynamic = "force-dynamic";
export const metadata = { title: "Document" };

type DocumentPageProps = {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ q?: string; documentError?: string; documentStatus?: string }>;
};

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export default async function DocumentPage({ params, searchParams }: DocumentPageProps) {
  const { id } = await params;
  if (!guid.test(id)) {
    notFound();
  }
  const query = await searchParams;
  const user = await requireWorkbenchUser(`/documents/${id}`);
  const document = await getDocument(id);
  if (!document) {
    notFound();
  }

  const canWrite = hasAnyRole(user, ["Admin", "Operator"]);
  const open = canActOnDocument(document);
  const onReviewList = (open && !document.rejectedAt) || document.stuck;
  const typeLabel = documentTypeLabel(document.documentType);
  const reason = formatReviewReason(document.reviewReason, document.state);
  const q = query.q?.trim() ?? "";
  const search = open && canWrite && q ? await searchReviewInvoices(q) : null;

  return (
    <RecordPage
      back={onReviewList ? { href: "/documents/review", label: "Review list" } : { href: "/documents", label: "Documents" }}
      header={{
        eyebrow: typeLabel,
        title: document.matchedReference ?? (onReviewList ? "Document to review" : "Unfiled document"),
        description: `Uploaded ${formatDateTime(document.uploadedAt)} by ${document.uploadedBy} · ${pages(document.pageCount)}`,
        actions: <DocumentStateBadge state={document.state} stuck={document.stuck} />,
      }}
      notice={
        <>
          {query.documentError ? (
            <Notice tone="error" title="Nothing changed" live>
              {query.documentError}
            </Notice>
          ) : null}
          {query.documentStatus ? <Notice tone="success" title={query.documentStatus} live /> : null}
          <StateNotice document={document} reason={reason} />
          {open && !canWrite ? (
            <Notice tone="info" title="Only an Admin or Operator can file or reject documents">
              You can see the document and what was read.
            </Notice>
          ) : null}
        </>
      }
      summary={
        <KeyValueList
          items={[
            { label: "State", value: formatDocumentState(document.state, document.stuck) },
            ...(reason ? [{ label: "Reason", value: reason }] : []),
            { label: "Invoice", value: document.matchedReference, mono: true },
            { label: "Client", value: document.matchedCustomerName },
            { label: "Filed by", value: document.matchedBy ? matchedByLabel(document.matchedBy) : null },
            { label: "Acumatica", value: acumaticaLabel(document) },
            { label: "Uploaded by", value: document.uploadedBy },
          ]}
        />
      }
      activity={<Timeline label="Document history" entries={history(document)} />}
    >
      <Columns>
        <Panel title="Document">
          <PdfFrame src={`/documents/${document.id}/file`} title={`${typeLabel} uploaded ${formatDateTime(document.uploadedAt)}`} />
        </Panel>
        <Stack gap="m">
          <Panel title="What was read" description="The fields read from the PDF.">
            <ExtractedFields document={document} />
          </Panel>
          {open && canWrite ? (
            <>
              <Panel
                flush
                title="Possible invoices"
                description="The invoices the backend found for the numbers on the document."
              >
                <DataTable
                  caption="Possible invoices"
                  columns={withFileAction(document)}
                  rows={document.candidates}
                  rowKey={(row) => row.referenceNumber}
                  empty={<EmptyState title="No possible invoices" description="Search for the invoice below." />}
                />
              </Panel>
              <Panel title="Find an invoice" description="Search Acumatica by invoice number or customer name.">
                <form method="get" action={`/documents/${document.id}`} role="search" aria-label="Find an invoice">
                  <Stack direction="row" gap="s" align="end" wrap>
                    <TextField name="q" label="Invoice or customer" type="search" width="m" showOptional={false} defaultValue={q} required />
                    <Button type="submit">Search invoices</Button>
                  </Stack>
                </form>
              </Panel>
              {search ? <SearchResults document={document} q={q} search={search} /> : null}
              {document.rejectedAt ? null : (
              <Panel title="Reject the document" description="For a bad scan or a document that is not a delivery note. It leaves the review list.">
                <ConfirmDialog
                  triggerLabel="Reject"
                  triggerVariant="tertiary"
                  title="Reject this document?"
                  confirmLabel="Reject document"
                  pendingLabel="Rejecting…"
                  action={rejectDocumentAction}
                  fields={{ documentId: document.id }}
                >
                  <p>The document leaves the review list and is not filed. You can still find it on the Documents page.</p>
                  <TextareaField
                    name="note"
                    label="Note"
                    hint="Why it is rejected, such as “Bad scan, driver asked to scan again”. At most 1000 characters."
                    width="full"
                    maxLength={1000}
                    required
                  />
                </ConfirmDialog>
              </Panel>
              )}
            </>
          ) : null}
        </Stack>
      </Columns>
    </RecordPage>
  );
}

function pages(count: number) {
  return count === 1 ? "1 page" : `${count} pages`;
}

function matchedByLabel(matchedBy: string) {
  return matchedBy === "auto" ? "Matched automatically" : matchedBy.replace(/^user:/, "");
}

function StateNotice({ document, reason }: { document: DocumentDetail; reason: string | null }) {
  if (document.stuck) {
    return (
      <Notice tone="warning" title="Stuck — processing did not finish">
        The document was uploaded {formatDateTime(document.uploadedAt)} and has not been read yet. There is nothing to file
        or reject. Tell the backoffice developer if it stays stuck.
      </Notice>
    );
  }
  if (document.rejectedAt) {
    return (
      <Notice tone="info" title={`Rejected by ${document.rejectedBy ?? "someone"} on ${formatDateTime(document.rejectedAt)}`}>
        {document.reviewNote}
        {document.state === "NeedsReview" || document.state === "Failed"
          ? " If it was rejected by mistake, an Admin or Operator can still file it to an invoice."
          : null}
      </Notice>
    );
  }
  if (document.state === "NeedsReview" || document.state === "Failed") {
    return (
      <Notice tone={document.state === "Failed" ? "error" : "warning"} title={reason ?? "Processing failed after every retry"}>
        {document.unreadableReason ? `${document.unreadableReason} ` : null}
        Check the PDF, then file it to the right invoice, or reject it with a note.
      </Notice>
    );
  }
  return null;
}

/** The candidate columns with the file action last. */
function withFileAction(document: DocumentDetail): Column<InvoiceCandidate>[] {
  return [
    ...invoiceColumns,
    {
      key: "action",
      header: "Action",
      render: (row) => <FileToInvoice document={document} invoice={row} />,
    },
  ];
}

function FileToInvoice({ document, invoice }: { document: DocumentDetail; invoice: InvoiceCandidate }) {
  return (
    <ConfirmDialog
      triggerLabel="File to this invoice"
      triggerVariant="secondary"
      title={`File this document to ${invoice.referenceNumber}?`}
      confirmLabel="File to this invoice"
      pendingLabel="Filing…"
      action={fileDocumentToAction}
      fields={{ documentId: document.id, invoiceReference: invoice.referenceNumber }}
    >
      <KeyValueList
        layout="stacked"
        items={[
          { label: "Invoice", value: invoice.referenceNumber, mono: true },
          { label: "Customer", value: customerOf(invoice) },
          { label: "Invoice date", value: invoice.date ? formatDate(invoice.date) : null },
        ]}
      />
      <p>The PDF is attached to this invoice in Acumatica. The document leaves the review list.</p>
    </ConfirmDialog>
  );
}

function SearchResults({
  document,
  q,
  search,
}: {
  document: DocumentDetail;
  q: string;
  search: Awaited<ReturnType<typeof searchReviewInvoices>>;
}) {
  if (!search.ok) {
    return (
      <Notice tone="error" title="The invoice search did not work">
        {search.status === 403
          ? "You do not have permission to search invoices. Ask an Admin or Operator."
          : `Acumatica did not answer (error ${search.status}). Try again in a minute.`}
      </Notice>
    );
  }
  return (
    <Panel flush title={`Invoices for “${q}”`} description="At most 20, newest first. Voided and cancelled invoices are left out.">
      <DataTable
        caption={`Invoices for ${q}`}
        columns={withFileAction(document)}
        rows={search.value}
        rowKey={(row) => row.referenceNumber}
        empty={<EmptyState title="No invoices match this search" description="Check the number, or search by customer name." />}
      />
    </Panel>
  );
}

function ExtractedFields({ document }: { document: DocumentDetail }) {
  const extraction = document.extraction;
  if (!extraction) {
    return (
      <EmptyState
        title="Nothing was read"
        description={document.unreadableReason ?? (document.state === "Received" || document.state === "Processing" ? "The document has not been read yet." : "The extractor returned no fields.")}
      />
    );
  }
  const list = (key: string): ReactNode => {
    const value = readExtraction(extraction, key);
    return Array.isArray(value) && value.length > 0 ? value.map(String).join(", ") : null;
  };
  const text = (key: string): ReactNode => {
    const value = readExtraction(extraction, key);
    return typeof value === "string" && value.trim() ? value : null;
  };
  const signature = readExtraction(extraction, "signaturePresent");
  const deliveryDate = text("deliveryDate");

  return (
    <KeyValueList
      layout="stacked"
      items={[
        { label: "Invoice numbers", value: list("invoiceNumbers"), mono: true },
        { label: "Sales order numbers", value: list("salesOrderNumbers"), mono: true },
        { label: "Customer order numbers", value: list("customerOrderNumbers"), mono: true },
        { label: "Client name", value: text("clientName") },
        { label: "Delivery date", value: typeof deliveryDate === "string" ? formatDate(deliveryDate) : null },
        { label: "Signed", value: signature === true ? "Yes" : signature === false ? "No" : null },
        { label: "Note", value: text("note") },
      ]}
    />
  );
}

function history(document: DocumentDetail): TimelineEntry[] {
  // order breaks ties: on the same instant, the later step shows first.
  const entries: (TimelineEntry & { at: string; order: number })[] = [
    {
      id: "uploaded",
      order: 0,
      at: document.uploadedAt,
      time: formatDateTime(document.uploadedAt),
      dateTime: document.uploadedAt,
      title: "Uploaded",
      detail: document.uploadedBy,
    },
  ];
  if (document.state === "Filed") {
    // An automatic match has no resolver; its last change is the closest time the API gives.
    const at = document.resolvedAt ?? document.acumaticaAttachedAt ?? document.updatedAt;
    entries.push({
      id: "filed",
      order: 1,
      at,
      time: formatDateTime(at),
      dateTime: at,
      title: `Filed to ${document.matchedReference ?? "an invoice"}`,
      detail: document.matchedBy ? matchedByLabel(document.matchedBy) : undefined,
    });
  }
  if (document.rejectedAt) {
    entries.push({
      id: "rejected",
      order: 2,
      at: document.rejectedAt,
      time: formatDateTime(document.rejectedAt),
      dateTime: document.rejectedAt,
      title: "Rejected",
      detail: document.rejectedBy ?? undefined,
    });
  }
  if (document.acumaticaAttachedAt) {
    entries.push({
      id: "attached",
      order: 3,
      at: document.acumaticaAttachedAt,
      time: formatDateTime(document.acumaticaAttachedAt),
      dateTime: document.acumaticaAttachedAt,
      title: "Attached to the invoice in Acumatica",
    });
  }
  return entries
    .sort((a, b) => Date.parse(b.at) - Date.parse(a.at) || b.order - a.order)
    .map((entry) => ({ id: entry.id, time: entry.time, dateTime: entry.dateTime, title: entry.title, detail: entry.detail }));
}
