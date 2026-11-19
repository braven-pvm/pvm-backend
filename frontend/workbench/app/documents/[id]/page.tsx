import { notFound } from "next/navigation";
import type { ReactNode } from "react";
import {
  acumaticaBaseUrl,
  getDocument,
  getGeneralDocuments,
  getReviewList,
  searchDocuments,
  searchReviewInvoices,
  type DocumentDetail,
  type InvoiceCandidate,
} from "../../../src/api/documents";
import { hasAnyRole, requireWorkbenchUser } from "../../../src/auth/session";
import {
  documentHref,
  findNeighbours,
  listContextFields,
  listHref,
  parseListContext,
  positionText,
  type ListContext,
  type Neighbours,
} from "../../../src/document-list";
import {
  acumaticaAttachLabel,
  canActOnDocument,
  canProcessAgain,
  canReturnToReview,
  deleteWarningTitle,
  documentTypeLabel,
  fileToAttachText,
  formatDocumentState,
  formatReviewReason,
  isDocumentId,
  linkedToText,
  maxChangeReasonLength,
  maxDeleteReasonLength,
  readByVisionTitle,
  readExtraction,
  showChangeInvoiceAction,
  showDeleteAction,
  uploaderText,
} from "../../../src/documents";
import { formatDate, formatDateTime } from "../../../src/formatters.mjs";
import { generalTitle, isGeneralDocument } from "../../../src/general-documents";
import {
  Button,
  ButtonLink,
  ConfirmDialog,
  DataTable,
  EmptyState,
  KeyValueList,
  Notice,
  Panel,
  PdfFrame,
  RecordNav,
  Stack,
  TextareaField,
  TextField,
  Timeline,
  ViewerPage,
  type Column,
  type TimelineEntry,
} from "../../../src/ui";
import {
  changeInvoiceAction,
  deleteDocumentAction,
  fileDocumentToAction,
  processAgainAction,
  rejectDocumentAction,
  returnToReviewAction,
} from "../actions";
import { AcumaticaInvoice, DocumentStateBadge, customerOf, invoiceColumns } from "../document-parts";

export const dynamic = "force-dynamic";
export const metadata = { title: "Document" };

type DocumentPageProps = {
  params: Promise<{ id: string }>;
  /**
   * q: the invoice search. change=1: the invoice search to change the invoice of a filed POD.
   * documentError, documentStatus, documentWarning (documentWarningFile): the result of an action.
   * list and the list's filters and page: the list the page was opened from (src/document-list.ts).
   */
  searchParams: Promise<Record<string, string | string[] | undefined>>;
};

/** Hidden form fields: the document, and the list context so an action can keep it. */
type Fields = Record<string, string>;

function param(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}

/** The documents before and after this one in the list it was opened from. Null when it is not on it. */
async function neighboursIn(context: ListContext | null, id: string): Promise<Neighbours | null> {
  if (!context) {
    return null;
  }
  try {
    return await findNeighbours(id, context.page, (page) => {
      switch (context.kind) {
        case "review":
          return getReviewList(page);
        case "general":
          return getGeneralDocuments({ ...context.filters, page });
        default:
          return searchDocuments({ ...context.filters, page });
      }
    });
  } catch (error) {
    // The page still works without Previous and Next.
    console.warn("documents.neighbours.unavailable", { error: String(error) });
    return null;
  }
}

export default async function DocumentPage({ params, searchParams }: DocumentPageProps) {
  const { id } = await params;
  if (!isDocumentId(id)) {
    notFound();
  }
  const query = await searchParams;
  const context = parseListContext(query);
  const user = await requireWorkbenchUser(documentHref(id, context));
  const [document, neighbours] = await Promise.all([getDocument(id), neighboursIn(context, id)]);
  if (!document) {
    notFound();
  }

  const canWrite = hasAnyRole(user, ["Admin", "Operator"]);
  if (isGeneralDocument(document.documentType)) {
    return <GeneralDocumentView document={document} context={context} neighbours={neighbours} canWrite={canWrite} query={query} />;
  }
  const open = canActOnDocument(document);
  const onReviewList = (open && !document.rejectedAt) || document.stuck;
  const typeLabel = documentTypeLabel(document.documentType);
  const reason = formatReviewReason(document.reviewReason, document.state);
  const q = param(query.q)?.trim() ?? "";
  const documentError = param(query.documentError);
  const documentStatus = param(query.documentStatus);
  // After a delete from the review list, the next document opens with the notice.
  const documentWarning = param(query.documentWarning);
  // Change invoice (design 6.13): the same invoice search, opened from the Actions panel.
  const canChange = showChangeInvoiceAction(document, canWrite);
  const changing = canChange && param(query.change) === "1";
  const search = (open || changing) && canWrite && q ? await searchReviewInvoices(q) : null;
  const contextFields = listContextFields(context);
  const fields: Fields = { documentId: document.id, ...contextFields };
  const searchFields: Fields = changing ? { ...contextFields, change: "1" } : contextFields;
  const step = (neighbour: { id: string; page: number } | null) =>
    neighbour && context ? documentHref(neighbour.id, { ...context, page: neighbour.page }) : undefined;

  const canReject = open && canWrite && !document.rejectedAt;
  const canSendBack = canWrite && canReturnToReview(document);
  const canProcess = canWrite && canProcessAgain(document);
  const canDelete = showDeleteAction(document, canWrite);

  return (
    <ViewerPage
      back={{ href: listHref(context), label: backLabel(context) }}
      nav={
        neighbours ? (
          <RecordNav
            noun="document"
            position={positionText(neighbours.position, neighbours.total)}
            previousHref={step(neighbours.previous)}
            nextHref={step(neighbours.next)}
          />
        ) : null
      }
      header={{
        eyebrow: typeLabel,
        title: document.matchedReference ?? (document.stuck ? "Document being processed" : onReviewList ? "Document to review" : "Unfiled document"),
        status: <DocumentStateBadge state={document.state} stuck={document.stuck} />,
        description: `Uploaded ${formatDateTime(document.uploadedAt)} by ${uploaderText(document)} · ${pages(document.pageCount)}`,
      }}
      viewer={
        <PdfFrame size="fill" src={`/documents/${document.id}/file`} title={`${typeLabel} uploaded ${formatDateTime(document.uploadedAt)}`} />
      }
    >
      {documentError ? (
        <Notice tone="error" title="Nothing changed" live>
          {documentError}
        </Notice>
      ) : null}
      {documentStatus ? <Notice tone="success" title={documentStatus} live /> : null}
      {documentWarning ? (
        <Notice tone="warning" title={deleteWarningTitle(param(query.documentWarningFile))}>
          {documentWarning}
        </Notice>
      ) : null}
      <StateNotice document={document} reason={reason} />
      {document.state === "Filed" && document.matchedReference ? (
        <Notice tone="info" title={linkedToText(document.matchedReference)}>
          {document.matchedCustomerName ? `${document.matchedCustomerName}. ` : null}
          {document.matchedBy
            ? document.matchedBy === "auto"
              ? "Matched automatically."
              : `Linked by ${matchedByLabel(document.matchedBy)}.`
            : null}
        </Notice>
      ) : null}
      {(open || document.stuck) && !canWrite ? (
        <Notice tone="info" title="Only an Admin or Operator can file, reject or process documents">
          You can see the document and what was read.
        </Notice>
      ) : null}
      {canReject || canSendBack || canProcess || canDelete || canChange ? (
        <Panel
          title="Actions"
          description={open && canWrite ? "To file it, use File to this invoice on one of the invoices below." : undefined}
        >
          <Stack direction="row" gap="s" wrap>
            {canChange && !changing ? (
              <ButtonLink href={changeInvoiceHref(document.id, contextFields)}>Change invoice</ButtonLink>
            ) : null}
            {canReject ? <RejectDocument fields={fields} /> : null}
            {canSendBack ? <SendBackToReview document={document} fields={fields} /> : null}
            {canProcess ? <ProcessAgain document={document} fields={fields} /> : null}
            {canDelete ? <DeleteDocument fields={fields} /> : null}
          </Stack>
        </Panel>
      ) : null}
      {open && canWrite ? (
        <>
          <Panel flush title="Possible invoices" description="The invoices the backend found for the numbers on the document.">
            <DataTable
              caption="Possible invoices"
              columns={withFileAction(document, fields)}
              rows={document.candidates}
              rowKey={(row) => row.referenceNumber}
              empty={<EmptyState title="No possible invoices" description="Search for the invoice below." />}
            />
          </Panel>
          <FindInvoice document={document} hidden={searchFields} q={q} />
          {search ? <SearchResults document={document} fields={fields} q={q} search={search} /> : null}
        </>
      ) : null}
      {changing ? (
        <>
          <FindInvoice
            document={document}
            hidden={searchFields}
            q={q}
            title="Change invoice"
            description={`Linked to ${document.matchedReference ?? "an invoice"} now. Search Acumatica by invoice number or customer name, then choose the right invoice.`}
            cancelHref={documentHref(document.id, context)}
          />
          {search ? <SearchResults document={document} fields={fields} q={q} search={search} change /> : null}
        </>
      ) : null}
      <Panel title="What was read" description="The fields read from the PDF.">
        <ExtractedFields document={document} />
      </Panel>
      <Panel title="Summary">
        <KeyValueList
          items={[
            { label: "State", value: formatDocumentState(document.state, document.stuck) },
            ...(reason ? [{ label: "Reason", value: reason }] : []),
            // The filing facts only mean something once the document is filed.
            ...(document.state === "Filed"
              ? [
                  {
                    label: "Linked to",
                    value: document.matchedReference ? (
                      <AcumaticaInvoice reference={document.matchedReference} baseUrl={acumaticaBaseUrl()} />
                    ) : null,
                    mono: true,
                  },
                  { label: "Client", value: document.matchedCustomerName },
                  { label: "Linked by", value: document.matchedBy ? matchedByLabel(document.matchedBy) : null },
                  // The attach state means something only when attachments are written (design 6.13).
                  ...(acumaticaAttachLabel(document, document.acumaticaAttachMode)
                    ? [{ label: "Acumatica", value: acumaticaAttachLabel(document, document.acumaticaAttachMode) }]
                    : []),
                ]
              : []),
            { label: "Uploaded by", value: uploaderText(document) },
          ]}
        />
      </Panel>
      <Panel title="Activity">
        <Timeline label="Document history" entries={history(document)} />
      </Panel>
    </ViewerPage>
  );
}

/** The document page with the Change invoice search open, keeping the list context. */
function changeInvoiceHref(id: string, contextFields: Fields) {
  return `/documents/${id}?${new URLSearchParams({ ...contextFields, change: "1" }).toString()}`;
}

function FindInvoice({
  document,
  hidden,
  q,
  title = "Find an invoice",
  description = "Search Acumatica by invoice number or customer name.",
  cancelHref,
}: {
  document: DocumentDetail;
  hidden: Fields;
  q: string;
  title?: string;
  description?: string;
  cancelHref?: string;
}) {
  return (
    <Panel title={title} description={description}>
      <form method="get" action={`/documents/${document.id}`} role="search" aria-label={title}>
        {Object.entries(hidden).map(([name, value]) => (
          <input key={name} type="hidden" name={name} value={value} />
        ))}
        <Stack direction="row" gap="s" align="end" wrap>
          <TextField name="q" label="Invoice or customer" type="search" width="m" showOptional={false} defaultValue={q} required />
          <Button type="submit">Search invoices</Button>
          {cancelHref ? (
            <ButtonLink href={cancelHref} variant="tertiary">
              Cancel
            </ButtonLink>
          ) : null}
        </Stack>
      </form>
    </Panel>
  );
}

function backLabel(context: ListContext | null) {
  switch (context?.kind) {
    case "review":
      return "Back to the review list";
    case "general":
      return "Back to General scans";
    default:
      return "Back to Documents";
  }
}

/**
 * A general document (design 6.12): the PDF, what was typed on the phone and
 * what Vision read, who scanned it, and Delete. It is never filed to an
 * invoice, so there is no File, Reject or review action.
 */
function GeneralDocumentView({
  document,
  context,
  neighbours,
  canWrite,
  query,
}: {
  document: DocumentDetail;
  context: ListContext | null;
  neighbours: Neighbours | null;
  canWrite: boolean;
  query: Record<string, string | string[] | undefined>;
}) {
  const title = generalTitle(document);
  const documentError = param(query.documentError);
  const documentStatus = param(query.documentStatus);
  const fields: Fields = { documentId: document.id, ...listContextFields(context) };
  const step = (neighbour: { id: string; page: number } | null) =>
    neighbour && context ? documentHref(neighbour.id, { ...context, page: neighbour.page }) : undefined;
  const words = document.searchWords ?? [];
  const read = Boolean(document.autoTitle || document.summary || document.documentDate || words.length > 0);
  const reading = document.state === "Received" || document.state === "Processing";

  return (
    <ViewerPage
      back={{ href: listHref(context), label: backLabel(context) }}
      nav={
        neighbours ? (
          <RecordNav
            noun="scan"
            position={positionText(neighbours.position, neighbours.total)}
            previousHref={step(neighbours.previous)}
            nextHref={step(neighbours.next)}
          />
        ) : null
      }
      header={{
        eyebrow: "General document",
        title,
        // Like the POD page: the state badge next to the title, Filed included (design 6.13).
        status: <DocumentStateBadge state={document.state} stuck={document.stuck} />,
        description: `Uploaded ${formatDateTime(document.uploadedAt)} by ${uploaderText(document)} · ${pages(document.pageCount)}`,
      }}
      viewer={<PdfFrame size="fill" src={`/documents/${document.id}/file`} title={`${title}, uploaded ${formatDateTime(document.uploadedAt)}`} />}
    >
      {documentError ? (
        <Notice tone="error" title="Nothing changed" live>
          {documentError}
        </Notice>
      ) : null}
      {documentStatus ? <Notice tone="success" title={documentStatus} live /> : null}
      {param(query.documentWarning) ? (
        <Notice tone="warning" title={deleteWarningTitle(param(query.documentWarningFile))}>
          {param(query.documentWarning)}
        </Notice>
      ) : null}
      {document.stuck ? (
        <Notice tone="warning" title="Stuck — reading did not finish">
          The scan is stored, but Vision has not read it yet. Tell the backoffice developer if it stays like this.
        </Notice>
      ) : document.state === "Failed" ? (
        <Notice tone="error" title="Reading failed after every retry">
          The scan is stored, but it has no automatic title or search words.
        </Notice>
      ) : null}
      <Panel title="Scan">
        <KeyValueList
          items={[
            { label: "Title", value: document.title?.trim() || null },
            { label: "Note", value: document.note?.trim() || null },
            { label: "Uploaded by", value: uploaderText(document) },
            { label: "Uploaded", value: formatDateTime(document.uploadedAt) },
          ]}
        />
      </Panel>
      <Panel title={readByVisionTitle} description="An automatic title and search words, so the scan can be found again.">
        {read ? (
          <KeyValueList
            layout="stacked"
            items={[
              { label: "Auto title", value: document.autoTitle },
              { label: "Summary", value: document.summary },
              { label: "Document date", value: document.documentDate ? formatDate(document.documentDate) : null },
              { label: "Search words", value: words.length > 0 ? words.join(", ") : null },
            ]}
          />
        ) : (
          <EmptyState
            title={reading ? "Not read yet" : "Nothing was read"}
            description={reading ? "Vision reads the scan in a minute or two." : "The scan could not be read. Search for it by its title, note, phone or date."}
          />
        )}
      </Panel>
      {showDeleteAction(document, canWrite) ? (
        <Panel title="Actions">
          <DeleteDocument fields={fields} general />
        </Panel>
      ) : null}
    </ViewerPage>
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
        The document was uploaded {formatDateTime(document.uploadedAt)} and has not been read yet. An Admin or Operator can
        process it again. Tell the backoffice developer if it stays stuck.
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
  if (document.state === "NeedsReview" && !document.reviewReason && document.reviewNote) {
    return (
      <Notice tone="warning" title={`Sent back to review by ${document.resolvedBy ?? "someone"}`}>
        {document.reviewNote} Check the PDF, then file it to the right invoice, or reject it with a note.
      </Notice>
    );
  }
  if (document.state === "Failed") {
    return (
      <Notice tone="error" title={reason ?? "Processing failed after every retry"}>
        {document.unreadableReason ? `${document.unreadableReason} ` : null}
        Process it again once the cause is fixed, file it to the right invoice, or reject it with a note.
      </Notice>
    );
  }
  if (document.state === "NeedsReview") {
    return (
      <Notice tone="warning" title={reason ?? "Needs review"}>
        {document.unreadableReason ? `${document.unreadableReason} ` : null}
        Check the PDF, then file it to the right invoice, or reject it with a note.
      </Notice>
    );
  }
  return null;
}

/** The candidate columns with the file action (or, when changing the invoice, the change action) last. */
function withFileAction(document: DocumentDetail, fields: Fields, change = false): Column<InvoiceCandidate>[] {
  return [
    ...invoiceColumns,
    {
      key: "action",
      header: "Action",
      render: (row) =>
        change ? (
          row.referenceNumber.toUpperCase() === document.matchedReference?.toUpperCase() ? (
            "Linked now"
          ) : (
            <ChangeToInvoice document={document} invoice={row} fields={fields} />
          )
        ) : (
          <FileToInvoice document={document} invoice={row} fields={fields} />
        ),
    },
  ];
}

/**
 * Change invoice (design 6.13): names the old and the new invoice and
 * customer. Nothing is written to Acumatica; an optional reason goes to the audit.
 */
function ChangeToInvoice({ document, invoice, fields }: { document: DocumentDetail; invoice: InvoiceCandidate; fields: Fields }) {
  return (
    <ConfirmDialog
      triggerLabel="Link to this invoice"
      triggerVariant="secondary"
      title={`Change the invoice to ${invoice.referenceNumber}?`}
      confirmLabel="Change invoice"
      pendingLabel="Changing…"
      action={changeInvoiceAction}
      fields={{ ...fields, invoiceReference: invoice.referenceNumber }}
    >
      <KeyValueList
        layout="stacked"
        items={[
          { label: "Linked to now", value: document.matchedReference, mono: true },
          { label: "Customer now", value: document.matchedCustomerName },
          { label: "New invoice", value: invoice.referenceNumber, mono: true },
          { label: "New customer", value: customerOf(invoice) },
          { label: "Invoice date", value: invoice.date ? formatDate(invoice.date) : null },
        ]}
      />
      <p>The document is linked to {invoice.referenceNumber} instead. Nothing changes in Acumatica.</p>
      <TextareaField
        name="reason"
        label="Reason"
        hint={`Why the invoice changes, such as “Split delivery, this page belongs to the second invoice”. At most ${maxChangeReasonLength} characters.`}
        width="full"
        maxLength={maxChangeReasonLength}
      />
    </ConfirmDialog>
  );
}

function FileToInvoice({ document, invoice, fields }: { document: DocumentDetail; invoice: InvoiceCandidate; fields: Fields }) {
  return (
    <ConfirmDialog
      triggerLabel="File to this invoice"
      triggerVariant="secondary"
      title={`File this document to ${invoice.referenceNumber}?`}
      confirmLabel="File to this invoice"
      pendingLabel="Filing…"
      action={fileDocumentToAction}
      fields={{ ...fields, invoiceReference: invoice.referenceNumber }}
    >
      <KeyValueList
        layout="stacked"
        items={[
          { label: "Invoice", value: invoice.referenceNumber, mono: true },
          { label: "Customer", value: customerOf(invoice) },
          { label: "Invoice date", value: invoice.date ? formatDate(invoice.date) : null },
        ]}
      />
      <p>{fileToAttachText(document.acumaticaAttachMode)} The document leaves the review list.</p>
    </ConfirmDialog>
  );
}

function RejectDocument({ fields }: { fields: Fields }) {
  return (
    <ConfirmDialog
      triggerLabel="Reject"
      triggerVariant="tertiary"
      title="Reject this document?"
      confirmLabel="Reject document"
      pendingLabel="Rejecting…"
      action={rejectDocumentAction}
      fields={fields}
    >
      <p>For a bad scan or a document that is not a delivery note.</p>
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
  );
}

function SendBackToReview({ document, fields }: { document: DocumentDetail; fields: Fields }) {
  return (
    <ConfirmDialog
      triggerLabel="Send back to review"
      triggerVariant="tertiary"
      title="Send this document back to review?"
      confirmLabel="Send back to review"
      pendingLabel="Sending back…"
      action={returnToReviewAction}
      fields={fields}
    >
      <p>For a document filed to the wrong invoice. Its PDF is not in Acumatica yet.</p>
      <p>
        The document is no longer filed to {document.matchedReference ?? "the invoice"} and its PDF will not be attached. It
        goes back on the review list, where it can be filed to the right invoice.
      </p>
      <TextareaField
        name="note"
        label="Note"
        hint="Why it goes back, such as “Filed to the wrong invoice of a split delivery”. At most 1000 characters."
        width="full"
        maxLength={1000}
        required
      />
    </ConfirmDialog>
  );
}

function ProcessAgain({ document, fields }: { document: DocumentDetail; fields: Fields }) {
  return (
    <ConfirmDialog
      triggerLabel="Process again"
      triggerVariant="secondary"
      title="Process this document again?"
      confirmLabel="Process again"
      pendingLabel="Queuing…"
      action={processAgainAction}
      fields={fields}
    >
      <p>{document.stuck ? "Processing did not finish. Send the document through it again." : "Processing failed. Send the document through it again."}</p>
      <p>The PDF is read and matched again. It is filed if the match is safe; otherwise it comes back to the review list.</p>
    </ConfirmDialog>
  );
}

function DeleteDocument({ fields, general = false }: { fields: Fields; general?: boolean }) {
  return (
    <ConfirmDialog
      triggerLabel={general ? "Delete scan" : "Delete document"}
      triggerVariant="tertiary"
      tone="danger"
      title={general ? "Delete this scan permanently?" : "Delete this document permanently?"}
      confirmLabel="Delete permanently"
      pendingLabel="Deleting…"
      action={deleteDocumentAction}
      fields={fields}
    >
      {general ? (
        <p>For a duplicate, a test scan or a paper that does not need keeping. A general scan is never in Acumatica.</p>
      ) : (
        <p>
          For a duplicate, a test upload or a scan that is not a delivery document. The document is not filed, so nothing in
          Acumatica changes.
        </p>
      )}
      <p>The PDF and its record are removed for good. This cannot be undone.</p>
      <TextareaField
        name="reason"
        label="Reason"
        hint={`Why it is deleted, such as “${general ? "Scanned twice" : "Duplicate scan of the same delivery note"}”. At most ${maxDeleteReasonLength} characters.`}
        width="full"
        maxLength={maxDeleteReasonLength}
        required
      />
    </ConfirmDialog>
  );
}

function SearchResults({
  document,
  fields,
  q,
  search,
  change = false,
}: {
  document: DocumentDetail;
  fields: Fields;
  q: string;
  search: Awaited<ReturnType<typeof searchReviewInvoices>>;
  change?: boolean;
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
        columns={withFileAction(document, fields, change)}
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
      detail: uploaderText(document),
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
      title: linkedToText(document.matchedReference ?? "an invoice"),
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
