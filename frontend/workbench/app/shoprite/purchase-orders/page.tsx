import { getPurchaseOrderFreshness, getPurchaseOrders, type PurchaseOrderSummary } from "../../../src/api/client";
import { hasAnyRole, requireWorkbenchUser } from "../../../src/auth/session";
import { formatDate, formatDateTime } from "../../../src/formatters.mjs";
import { ButtonLink, DataTable, EmptyState, FilterChips, ListPage, Notice, type Column } from "../../../src/ui";
import { ShopriteSections } from "../shoprite-sections";

export const dynamic = "force-dynamic";
export const metadata = { title: "Shoprite purchase orders" };

type PurchaseOrdersPageProps = {
  searchParams: Promise<{ type?: string }>;
};

const orderTypes = [
  { value: undefined, label: "All" },
  { value: "220", label: "Normal" },
  { value: "258", label: "Allocation" },
] as const;

const columns: Column<PurchaseOrderSummary>[] = [
  { key: "po", header: "PO", render: (row) => row.purchaseOrderNumber, mono: true },
  { key: "type", header: "Type", render: (row) => [row.orderTypeCode, row.orderTypeLabel].filter(Boolean).join(" · ") || "Unknown" },
  { key: "location", header: "Deliver to", render: (row) => row.deliveryLocationName ?? row.deliveryLocationCode ?? row.deliveryGln ?? "Unknown" },
  { key: "gln", header: "Delivery GLN", render: (row) => row.deliveryGln ?? "—", mono: true },
  { key: "lines", header: "Lines", render: (row) => row.lineCount, align: "end" },
  { key: "seen", header: "Last received", render: (row) => formatDateTime(row.lastSeenAt), weak: true },
];

export default async function PurchaseOrdersPage({ searchParams }: PurchaseOrdersPageProps) {
  const user = await requireWorkbenchUser("/shoprite/purchase-orders");
  const { type } = await searchParams;
  const [purchaseOrders, freshness] = await Promise.all([getPurchaseOrders(), getPurchaseOrderFreshness()]);
  const rows = type ? purchaseOrders.filter((order) => order.orderTypeCode === type) : purchaseOrders;

  return (
    <ListPage
      header={{
        eyebrow: "Shoprite",
        title: "Purchase orders",
        description: `Orders received before order reading was switched off. Last received: ${
          freshness.lastSuccessfulRefreshAt ? formatDate(freshness.lastSuccessfulRefreshAt) : "never"
        }.`,
      }}
      sections={<ShopriteSections current="purchase-orders" isAdmin={hasAnyRole(user, ["Admin"])} />}
      notice={
        <Notice tone="info" title="Reading Shoprite orders is switched off">
          <p>
            Shoprite marks an order as downloaded when anything reads it, which takes it away from the people who work
            orders on the Shoprite portal. This list no longer grows. Invoices get the store GLN and item GTINs from
            Shoprite reference data instead.
          </p>
        </Notice>
      }
      filters={
        <FilterChips
          label="Filter purchase orders by type"
          chips={orderTypes.map((orderType) => ({
            label: orderType.label,
            count: orderType.value ? purchaseOrders.filter((order) => order.orderTypeCode === orderType.value).length : purchaseOrders.length,
            href: orderType.value ? `/shoprite/purchase-orders?type=${orderType.value}` : "/shoprite/purchase-orders",
            current: type === orderType.value,
          }))}
        />
      }
      table={
        <DataTable
          caption="Shoprite purchase orders"
          columns={columns}
          rows={rows}
          rowKey={(row) => row.id}
          rowHref={(row) => `/shoprite/purchase-orders/${row.id}`}
          empty={
            purchaseOrders.length === 0 ? (
              <EmptyState
                title="No purchase orders stored"
                description="Invoices are completed from Shoprite reference data, so this list can stay empty."
              />
            ) : (
              <EmptyState
                title="No purchase orders of this type"
                action={<ButtonLink href="/shoprite/purchase-orders">Show all purchase orders</ButtonLink>}
              />
            )
          }
        />
      }
    />
  );
}
