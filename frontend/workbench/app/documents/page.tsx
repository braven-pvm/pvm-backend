import { acumaticaBaseUrl, getDocumentTypes, searchDocuments, type DocumentSearchItem } from "../../src/api/documents";
import { requireWorkbenchUser } from "../../src/auth/session";
import { documentHref } from "../../src/document-list";
import {
  buildDocumentSearchQuery,
  deleteWarningTitle,
  documentStates,
  documentTypeLabel,
  formatReviewReason,
  uploaderText,
  type DocumentSearchFilters,
} from "../../src/documents";
import { formatDateTime } from "../../src/formatters.mjs";
import { documentRowTitle, isGeneralDocument, noteLine } from "../../src/general-documents";
import {
  ButtonLink,
  DataTable,
  EmptyState,
  FilterBar,
  FilterChips,
  ListPage,
  Notice,
  Pagination,
  TextField,
  type Column,
} from "../../src/ui";
import { DocumentStateBadge, InvoiceLink } from "./document-parts";

export const dynamic = "force-dynamic";
export const metadata = { title: "Documents" };

type SearchParams = {
  invoice?: string;
  client?: string;
  type?: string;
  state?: string;
  from?: string;
  to?: string;
  page?: string;
  /** After a delete: the result notice, and a storage warning when a file stayed behind. */
  documentStatus?: string;
  documentWarning?: string;
  /** "pdf" or "thumbnail": the file the warning is about. */
  documentWarningFile?: string;
  documentError?: string;
};

// All types in one list (design 6.13). The state comes right after the first cell: on a phone
// the table scrolls sideways. The first cell links to the document page: a POD's invoice, or a
// general scan's title. "Details" says "Linked to INV…", why a POD needs review, or a scan's note.
function columnsFor(acumaticaUrl: string | undefined, mode: string | undefined): Column<DocumentSearchItem>[] {
  return [
    { key: "document", header: "Invoice or title", render: (row) => documentRowTitle(row) },
    { key: "state", header: "State", render: (row) => <DocumentStateBadge state={row.state} /> },
    { key: "client", header: "Client", render: (row) => row.matchedCustomerName ?? "—" },
    {
      key: "detail",
      header: "Details",
      render: (row) => {
        if (isGeneralDocument(row.documentType)) {
          return noteLine(row.note) ?? "—";
        }
        if (row.state === "Filed" && row.matchedReference) {
          return <InvoiceLink document={row} mode={mode} baseUrl={acumaticaUrl} />;
        }
        return formatReviewReason(row.reviewReason, row.state) ?? "—";
      },
      weak: true,
    },
    { key: "uploaded", header: "Uploaded", render: (row) => formatDateTime(row.uploadedAt), weak: true },
    { key: "type", header: "Document", render: (row) => documentTypeLabel(row.documentType), weak: true },
    { key: "by", header: "Uploaded by", render: (row) => uploaderText(row), weak: true },
  ];
}

export default async function DocumentsPage({ searchParams }: { searchParams: Promise<SearchParams> }) {
  await requireWorkbenchUser("/documents");
  const params = await searchParams;
  const filters: DocumentSearchFilters = {
    invoice: params.invoice,
    client: params.client,
    type: params.type,
    state: params.state,
    from: params.from,
    to: params.to,
  };
  const page = Math.max(Number(params.page) || 1, 1);
  const [result, types] = await Promise.all([searchDocuments({ ...filters, page }), getDocumentTypes()]);

  const href = (changes: Partial<DocumentSearchFilters>) => {
    const query = buildDocumentSearchQuery({ ...filters, ...changes }, { forApi: false });
    return query ? `/documents?${query}` : "/documents";
  };
  // The state is a chip, kept by the bar; the bar's own fields are counted for "Clear filters".
  const barCount = [filters.invoice, filters.client, filters.from, filters.to].filter((value) => value?.trim()).length;
  const anyFilter = barCount > 0 || Boolean(filters.state?.trim()) || Boolean(filters.type?.trim());
  const keep: Record<string, string> = {};
  if (filters.state) keep.state = filters.state;
  if (filters.type) keep.type = filters.type;

  const first = (result.page - 1) * result.pageSize + 1;
  const last = first + result.items.length - 1;

  return (
    <ListPage
      header={{
        title: "Documents",
        description:
          "Every scanned document: proofs of delivery and general scans. Search by invoice number when a client asks about a delivery.",
        actions: <ButtonLink href="/documents/review">Open the review list</ButtonLink>,
      }}
      notice={
        params.documentStatus || params.documentWarning || params.documentError ? (
          <>
            {params.documentError ? (
              <Notice tone="error" title="Nothing changed" live>
                {params.documentError}
              </Notice>
            ) : null}
            {params.documentStatus ? <Notice tone="success" title={params.documentStatus} live /> : null}
            {params.documentWarning ? (
              <Notice tone="warning" title={deleteWarningTitle(params.documentWarningFile)}>
                {params.documentWarning}
              </Notice>
            ) : null}
          </>
        ) : undefined
      }
      filters={
        <>
          <FilterChips
            label="Filter documents by state"
            chips={[
              { label: "All", href: href({ state: undefined }), current: !filters.state },
              ...documentStates.map((state) => ({
                label: state.label,
                href: href({ state: state.value }),
                current: filters.state?.toLowerCase() === state.value.toLowerCase(),
              })),
            ]}
          />
          <FilterChips
            label="Filter documents by type"
            chips={[
              { label: "All types", href: href({ type: undefined }), current: !filters.type },
              ...types.map((type) => ({
                label: type.displayName,
                href: href({ type: type.code }),
                current: filters.type?.toUpperCase() === type.code.toUpperCase(),
              })),
            ]}
          />
          <FilterBar action="/documents" label="Search documents" keep={keep} activeCount={barCount}>
            <TextField
              name="invoice"
              label="Invoice number"
              type="search"
              width="m"
              showOptional={false}
              defaultValue={filters.invoice}
            />
            <TextField name="client" label="Client" type="search" width="m" showOptional={false} defaultValue={filters.client} />
            <TextField name="from" label="Uploaded from" type="date" width="s" showOptional={false} defaultValue={filters.from} />
            <TextField name="to" label="Uploaded to" type="date" width="s" showOptional={false} defaultValue={filters.to} />
          </FilterBar>
        </>
      }
      table={
        <DataTable
          caption="Documents"
          columns={columnsFor(acumaticaBaseUrl(), result.acumaticaAttachMode)}
          rows={result.items}
          rowKey={(row) => row.id}
          // The link carries the search, so the document page can go back to it and step through it.
          rowHref={(row) => documentHref(row.id, { kind: "search", filters, page: result.page })}
          empty={
            anyFilter ? (
              <EmptyState
                title="No documents match this search"
                description="Check the invoice number. It must be the full number. To find a general scan by its words, use General scans."
                action={<ButtonLink href="/documents">Show all documents</ButtonLink>}
              />
            ) : (
              <EmptyState title="No documents yet" description="Documents appear here when someone scans a delivery note or a general document." />
            )
          }
        />
      }
      pagination={
        result.items.length > 0 ? (
          <Pagination
            from={first}
            to={last}
            total={result.total}
            previousHref={result.page > 1 ? href({ page: result.page - 1 }) : undefined}
            nextHref={last < result.total ? href({ page: result.page + 1 }) : undefined}
          />
        ) : null
      }
    />
  );
}
