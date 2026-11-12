import {
  ButtonLink,
  DataTable,
  EmptyState,
  FilterBar,
  FilterChips,
  ListPage,
  Notice,
  PageLoading,
  Pagination,
  SelectField,
  TabNav,
  TextField,
} from "../../../../src/ui";
import { devOnly, invoiceColumns, invoices } from "../sample";
import { ListErrorExample } from "./error-example";

export const metadata = { title: "List template" };

type Params = { status?: string; q?: string; store?: string; filters?: string; state?: string };

const statusChips = [
  { value: undefined, label: "All", count: 52 },
  { value: "Ready", label: "Ready", count: 51 },
  { value: "NeedsReview", label: "Needs review", count: 1 },
  { value: "Submitted", label: "Sent", count: 0 },
];

export default async function ListTemplateExample({ searchParams }: { searchParams: Promise<Params> }) {
  devOnly();
  const params = await searchParams;
  const base = "/ui-kit/templates/list";

  // The loading and error states, as loading.tsx and error.tsx show them.
  if (params.state === "loading") {
    return <PageLoading title="Shoprite invoices" label="Loading invoices…" />;
  }
  if (params.state === "error") {
    return <ListErrorExample />;
  }

  const query = (params.q ?? "").trim().toLowerCase();
  const rows = invoices.filter(
    (row) =>
      (!params.status || row.status === params.status) &&
      (!query || row.number.toLowerCase().includes(query) || row.store.toLowerCase().includes(query)),
  );
  const activeFilters = [params.q, params.store].filter(Boolean).length;

  return (
    <ListPage
      header={{
        eyebrow: "Shoprite",
        title: "Invoices",
        description: "Finalised Acumatica invoices for Shoprite, checked and ready to send.",
        actions: <ButtonLink href={`${base}?filters=bar`}>Show the filter bar example</ButtonLink>,
      }}
      sections={
        <TabNav
          label="Shoprite sections"
          tabs={[
            { label: "Invoices", href: base, current: true, count: 52 },
            { label: "Purchase orders", href: `${base}?tab=po`, current: false },
            { label: "Reference data", href: `${base}?tab=ref`, current: false },
          ]}
        />
      }
      notice={
        <Notice tone="info" title="Submission is in shadow mode">
          Invoices are checked and prepared, but nothing is sent to Shoprite.
        </Notice>
      }
      filters={
        params.filters === "bar" ? (
          <FilterBar action={base} label="Filter invoices" keep={{ filters: "bar" }} activeCount={activeFilters}>
            <TextField name="q" label="Invoice or store" type="search" width="m" showOptional={false} defaultValue={params.q} />
            <SelectField
              name="store"
              label="Store or DC"
              width="m"
              showOptional={false}
              placeholder="All stores"
              defaultValue={params.store}
              options={invoices.map((row) => ({ value: row.store, label: row.store }))}
            />
            <TextField name="from" label="From date" type="date" width="s" showOptional={false} />
          </FilterBar>
        ) : (
          <FilterChips
            label="Filter invoices by status"
            chips={statusChips.map((chip) => ({
              label: chip.label,
              count: chip.count,
              href: chip.value ? `${base}?status=${chip.value}` : base,
              current: params.status === chip.value,
            }))}
          />
        )
      }
      table={
        <DataTable
          caption="Shoprite invoices"
          columns={invoiceColumns}
          rows={rows}
          rowKey={(row) => row.number}
          rowHref={() => "/ui-kit/templates/record"}
          sort={{ key: "date", direction: "desc" }}
          sortHref={(key, direction) => `${base}?sort=${key}&dir=${direction}`}
          empty={
            <EmptyState
              title="No invoices match this filter"
              description="Choose All to see every invoice."
              action={<ButtonLink href={base}>Show all invoices</ButtonLink>}
            />
          }
        />
      }
      pagination={rows.length > 0 ? <Pagination from={1} to={rows.length} total={rows.length === invoices.length ? 52 : rows.length} nextHref={rows.length === invoices.length ? `${base}?page=2` : undefined} /> : null}
    />
  );
}
