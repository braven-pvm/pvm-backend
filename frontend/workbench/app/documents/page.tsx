import { acumaticaBaseUrl, getDocumentTypes, searchDocuments, type DocumentSearchItem } from "../../src/api/documents";
import { requireWorkbenchUser } from "../../src/auth/session";
import { buildDocumentSearchQuery, documentStates, documentTypeLabel, formatReviewReason, uploaderText, type DocumentSearchFilters } from "../../src/documents";
import { formatDateTime } from "../../src/formatters.mjs";
import {
  ButtonLink,
  DataTable,
  EmptyState,
  FilterBar,
  FilterChips,
  ListPage,
  Pagination,
  TextField,
  type Column,
} from "../../src/ui";
import { AcumaticaInvoice, DocumentStateBadge, acumaticaLabel } from "./document-parts";

export const dynamic = "force-dynamic";
export const metadata = { title: "Documents" };

type SearchParams = { invoice?: string; client?: string; type?: string; state?: string; from?: string; to?: string; page?: string };

// The state comes right after the invoice: on a phone the table scrolls sideways.
// The first cell links to the document page; the Acumatica link sits in its own column.
function columnsFor(acumaticaUrl: string | undefined): Column<DocumentSearchItem>[] {
  return [
    { key: "invoice", header: "Invoice", render: (row) => row.matchedReference ?? "Not filed", mono: true },
    { key: "state", header: "State", render: (row) => <DocumentStateBadge state={row.state} /> },
    { key: "client", header: "Client", render: (row) => row.matchedCustomerName ?? "—" },
    {
      key: "detail",
      header: "Acumatica or reason",
      render: (row) => {
        const attach = acumaticaLabel(row);
        if (attach && row.matchedReference) {
          return (
            <>
              <AcumaticaInvoice reference={row.matchedReference} baseUrl={acumaticaUrl} /> · {attach}
            </>
          );
        }
        return attach ?? formatReviewReason(row.reviewReason, row.state) ?? "—";
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
        description: "Delivery documents scanned at the office. Search by invoice number when a client asks about a delivery.",
        actions: <ButtonLink href="/documents/review">Open the review list</ButtonLink>,
      }}
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
          columns={columnsFor(acumaticaBaseUrl())}
          rows={result.items}
          rowKey={(row) => row.id}
          rowHref={(row) => `/documents/${row.id}`}
          empty={
            anyFilter ? (
              <EmptyState
                title="No documents match this search"
                description="Check the invoice number. It must be the full number."
                action={<ButtonLink href="/documents">Show all documents</ButtonLink>}
              />
            ) : (
              <EmptyState title="No documents yet" description="Documents appear here when a driver scans a delivery note." />
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
