import { RefreshCw } from "lucide-react";
import { refreshCandidatesAction } from "../../actions";
import { getInvoiceCandidates, type InvoiceCandidateSummary } from "../../../src/api/client";
import { hasAnyRole, requireWorkbenchUser } from "../../../src/auth/session";
import { formatDateTime } from "../../../src/formatters.mjs";
import { ButtonLink, DataTable, EmptyState, FilterChips, ListPage, StatusBadge, SubmitButton, type Column } from "../../../src/ui";
import { ShopriteSections } from "../shoprite-sections";

export const dynamic = "force-dynamic";
export const metadata = { title: "Shoprite invoices" };

type InvoicesPageProps = {
  searchParams: Promise<{ status?: string }>;
};

const filters = [
  { value: undefined, label: "All" },
  { value: "Ready", label: "Ready" },
  { value: "NeedsReview", label: "Needs review" },
  { value: "Submitted", label: "Submitted" },
] as const;

const columns: Column<InvoiceCandidateSummary>[] = [
  { key: "invoice", header: "Invoice", render: (row) => row.invoiceNumber, mono: true },
  { key: "customer", header: "Customer", render: (row) => row.customerAccount, mono: true },
  { key: "po", header: "Shoprite PO", render: (row) => row.shopritePurchaseOrderNumber ?? "Missing", mono: true },
  { key: "match", header: "PO match", render: (row) => <StatusBadge status={row.purchaseOrderMatchStatus} /> },
  { key: "location", header: "Store or DC", render: (row) => row.customerLocation ?? row.storeDcGln ?? "Not mapped" },
  { key: "updated", header: "Updated", render: (row) => formatDateTime(row.updatedAt), weak: true },
  { key: "status", header: "Status", render: (row) => <StatusBadge status={row.status} /> },
];

export default async function InvoiceCandidatesPage({ searchParams }: InvoicesPageProps) {
  const user = await requireWorkbenchUser("/shoprite/invoices");
  const { status } = await searchParams;
  const candidates = await getInvoiceCandidates();
  const canWrite = hasAnyRole(user, ["Admin", "Operator"]);
  const rows = status ? candidates.filter((candidate) => candidate.status === status) : candidates;

  return (
    <ListPage
      header={{
        eyebrow: "Shoprite",
        title: "Invoices",
        description: "Finalised Acumatica invoices for Shoprite. Each one is checked against Shoprite reference data before it can be sent.",
        actions: canWrite ? (
          <form action={refreshCandidatesAction}>
            <SubmitButton pendingLabel="Reading Acumatica…">
              <RefreshCw aria-hidden="true" />
              Read invoices from Acumatica
            </SubmitButton>
          </form>
        ) : null,
      }}
      sections={<ShopriteSections current="invoices" isAdmin={hasAnyRole(user, ["Admin"])} />}
      filters={
        <FilterChips
          label="Filter invoices by status"
          chips={filters.map((filter) => ({
            label: filter.label,
            count: filter.value ? candidates.filter((candidate) => candidate.status === filter.value).length : candidates.length,
            href: filter.value ? `/shoprite/invoices?status=${filter.value}` : "/shoprite/invoices",
            current: status === filter.value,
          }))}
        />
      }
      table={
        <DataTable
          caption="Shoprite invoices"
          columns={columns}
          rows={rows}
          rowKey={(row) => row.id}
          rowHref={(row) => `/shoprite/invoices/${row.id}`}
          empty={
            candidates.length === 0 ? (
              <EmptyState
                title="No Shoprite invoices yet"
                description="Finalised Acumatica invoices for Shoprite customers appear here after the next read, every 10 minutes."
              />
            ) : (
              <EmptyState
                title="No invoices match this filter"
                action={<ButtonLink href="/shoprite/invoices">Show all invoices</ButtonLink>}
              />
            )
          }
        />
      }
    />
  );
}
