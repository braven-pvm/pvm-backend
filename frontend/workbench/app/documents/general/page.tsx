import { getGeneralDocuments, getGeneralFilterOptions, type GeneralDocumentItem } from "../../../src/api/documents";
import { requireWorkbenchUser } from "../../../src/auth/session";
import { documentHref } from "../../../src/document-list";
import { buildGeneralQuery, generalTitle, noteLine, phoneAndStaff, type GeneralSearchFilters } from "../../../src/general-documents";
import { deleteWarningTitle } from "../../../src/documents";
import { formatDateTime } from "../../../src/formatters.mjs";
import {
  ButtonLink,
  DataTable,
  EmptyState,
  FilterBar,
  FilterChips,
  ListPage,
  Notice,
  Pagination,
  SelectField,
  Stack,
  TextField,
  Thumbnail,
  type Column,
} from "../../../src/ui";
import { DocumentStateBadge } from "../document-parts";

export const dynamic = "force-dynamic";
export const metadata = { title: "General scans" };

type SearchParams = {
  q?: string;
  device?: string;
  staff?: string;
  from?: string;
  to?: string;
  page?: string;
  /** After a delete: the result notice, and a storage warning when the PDF stayed behind. */
  documentStatus?: string;
  documentWarning?: string;
  /** "pdf" or "thumbnail": the file the warning is about. */
  documentWarningFile?: string;
  documentError?: string;
};

// The first cell is the link: the picture of the first page and the title.
const columns: Column<GeneralDocumentItem>[] = [
  {
    key: "scan",
    header: "Scan",
    render: (row) => {
      const title = generalTitle(row);
      return (
        <Stack direction="row" gap="s" align="center">
          <Thumbnail src={row.hasThumbnail ? `/documents/${row.id}/thumbnail` : null} alt={`First page of ${title}`} />
          <span>{title}</span>
        </Stack>
      );
    },
  },
  { key: "note", header: "Note", render: (row) => noteLine(row.note) ?? "—", weak: true },
  { key: "by", header: "Phone · staff", render: (row) => phoneAndStaff(row), weak: true },
  { key: "uploaded", header: "Uploaded", render: (row) => formatDateTime(row.uploadedAt), weak: true },
  // Filed is the normal end state; the badge tells a scan still being read or a failed one apart.
  { key: "state", header: "State", render: (row) => <DocumentStateBadge state={row.state} /> },
];

export default async function GeneralScansPage({ searchParams }: { searchParams: Promise<SearchParams> }) {
  await requireWorkbenchUser("/documents/general");
  const params = await searchParams;
  const filters: GeneralSearchFilters = {
    q: params.q,
    device: params.device,
    staff: params.staff,
    from: params.from,
    to: params.to,
  };
  const page = Math.max(Number(params.page) || 1, 1);
  const [result, options] = await Promise.all([getGeneralDocuments({ ...filters, page }), getGeneralFilterOptions()]);

  const href = (changes: Partial<GeneralSearchFilters>) => {
    const query = buildGeneralQuery({ ...filters, ...changes }, { forApi: false });
    return query ? `/documents/general?${query}` : "/documents/general";
  };
  // The phone is a chip, kept by the bar; the bar's own fields are counted for "Clear filters".
  const barCount = [filters.q, filters.staff, filters.from, filters.to].filter((value) => value?.trim()).length;
  const anyFilter = barCount > 0 || Boolean(filters.device?.trim());
  const keep: Record<string, string> = {};
  if (filters.device) keep.device = filters.device;

  const first = (result.page - 1) * result.pageSize + 1;
  const last = first + result.items.length - 1;
  // Kept in the document links: the document page then walks this same list.
  const listFilters = Object.fromEntries(
    Object.entries(filters).filter(([, value]) => typeof value === "string" && value.trim()),
  ) as GeneralSearchFilters;
  delete listFilters.page;

  return (
    <ListPage
      header={{
        title: "General scans",
        description: "Papers scanned on the phones to keep: letters, statements, forms. Search by what is on the paper.",
        actions: <ButtonLink href="/documents">Delivery documents</ButtonLink>,
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
          {options.devices.length > 0 ? (
            <FilterChips
              label="Filter scans by phone"
              chips={[
                { label: "All phones", href: href({ device: undefined, page: undefined }), current: !filters.device },
                ...options.devices.map((device) => ({
                  label: device.name,
                  href: href({ device: device.id, page: undefined }),
                  current: filters.device === device.id,
                })),
              ]}
            />
          ) : null}
          <FilterBar action="/documents/general" label="Search general scans" keep={keep} activeCount={barCount}>
            <TextField
              name="q"
              label="Title, note or words on the paper"
              type="search"
              width="m"
              showOptional={false}
              defaultValue={filters.q}
            />
            <SelectField
              name="staff"
              label="Staff"
              width="s"
              showOptional={false}
              placeholder="All staff"
              options={options.staff.map((staff) => ({ value: staff.id, label: staff.name }))}
              defaultValue={filters.staff ?? ""}
            />
            <TextField name="from" label="Uploaded from" type="date" width="s" showOptional={false} defaultValue={filters.from} />
            <TextField name="to" label="Uploaded to" type="date" width="s" showOptional={false} defaultValue={filters.to} />
          </FilterBar>
        </>
      }
      table={
        <DataTable
          caption="General scans"
          columns={columns}
          rows={result.items}
          rowKey={(row) => row.id}
          rowHref={(row) => documentHref(row.id, { kind: "general", filters: listFilters, page: result.page })}
          empty={
            anyFilter ? (
              <EmptyState
                title="No scans match this search"
                description="Try fewer words, or another phone, staff name or date."
                action={<ButtonLink href="/documents/general">Show all scans</ButtonLink>}
              />
            ) : (
              <EmptyState title="No general scans yet" description="Scans appear here when someone scans a General document on a phone." />
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
