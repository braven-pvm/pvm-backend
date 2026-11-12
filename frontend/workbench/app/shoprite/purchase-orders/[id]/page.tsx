import { notFound } from "next/navigation";
import { seedInvoiceFromPurchaseOrderAction } from "../../../actions";
import { getPurchaseOrder, type PurchaseOrderDetail } from "../../../../src/api/client";
import { hasAnyRole, requireWorkbenchUser } from "../../../../src/auth/session";
import { formatDateTime, formatMoneyZar } from "../../../../src/formatters.mjs";
import {
  CodeBlock,
  ConfirmDialog,
  DataTable,
  EmptyState,
  KeyValueList,
  Panel,
  RecordPage,
  StatusBadge,
  TabNav,
  Timeline,
  type Column,
} from "../../../../src/ui";

export const dynamic = "force-dynamic";
export const metadata = { title: "Shoprite purchase order" };

type PurchaseOrderDetailPageProps = {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ tab?: string }>;
};

type OrderLine = PurchaseOrderDetail["lines"][number];
type LinkedInvoice = PurchaseOrderDetail["linkedInvoiceCandidates"][number];

const lineColumns: Column<OrderLine>[] = [
  { key: "line", header: "Line", render: (line) => line.lineNumber, align: "end" },
  { key: "gtin", header: "GTIN", render: (line) => line.gtin ?? "—", mono: true },
  { key: "buyer", header: "Shoprite item", render: (line) => line.buyerItemId ?? "—", mono: true },
  { key: "description", header: "Description", render: (line) => line.description ?? line.buyerItemDescription ?? "—" },
  { key: "quantity", header: "Quantity", render: (line) => [line.requestedQuantity, line.measurementUnitCode].filter((part) => part !== null && part !== undefined).join(" ") || "—", align: "end" },
  { key: "price", header: "Net price", render: (line) => formatMoneyZar(line.netPrice), align: "end" },
];

const invoiceColumns: Column<LinkedInvoice>[] = [
  { key: "invoice", header: "Invoice", render: (row) => row.invoiceNumber, mono: true },
  { key: "customer", header: "Customer", render: (row) => row.customerAccount, mono: true },
  { key: "updated", header: "Updated", render: (row) => formatDateTime(row.updatedAt), weak: true },
  { key: "status", header: "Status", render: (row) => <StatusBadge status={row.status} /> },
];

export default async function PurchaseOrderDetailPage({ params, searchParams }: PurchaseOrderDetailPageProps) {
  const { id } = await params;
  const { tab: requestedTab } = await searchParams;
  const user = await requireWorkbenchUser(`/shoprite/purchase-orders/${id}`);
  const order = await loadPurchaseOrder(id);
  const canWrite = hasAnyRole(user, ["Admin", "Operator"]);
  const isAdmin = hasAnyRole(user, ["Admin"]);
  const tab = requestedTab === "invoices" || (requestedTab === "raw" && isAdmin) ? requestedTab : "lines";
  const base = `/shoprite/purchase-orders/${id}`;

  return (
    <RecordPage
      back={{ href: "/shoprite/purchase-orders", label: "Shoprite purchase orders" }}
      header={{
        eyebrow: "Shoprite purchase order",
        title: order.purchaseOrderNumber,
        description: [order.deliveryLocationName ?? "Unknown location", order.totalIncludingTax ? `${formatMoneyZar(order.totalIncludingTax)} incl. VAT` : null]
          .filter(Boolean)
          .join(" · "),
        actions: (
          <>
            <StatusBadge status={order.sourceEnvironment} tone="neutral" />
            {canWrite ? (
              <ConfirmDialog
                triggerLabel="Create test invoice"
                triggerVariant="secondary"
                title={`Create a test invoice from PO ${order.purchaseOrderNumber}?`}
                confirmLabel="Create test invoice"
                pendingLabel="Creating…"
                action={seedInvoiceFromPurchaseOrderAction}
                fields={{ id: order.id }}
              >
                <p>
                  This adds an invoice to the backoffice list for testing. It writes nothing to Acumatica and sends nothing
                  to Shoprite.
                </p>
              </ConfirmDialog>
            ) : null}
          </>
        ),
      }}
      summary={
        <KeyValueList
          items={[
            { label: "Order type", value: [order.orderTypeCode, order.orderTypeLabel].filter(Boolean).join(" · ") || null },
            { label: "Deliver to", value: [order.deliveryLocationCode, order.deliveryLocationName].filter(Boolean).join(" · ") || null },
            { label: "Delivery GLN", value: order.deliveryGln, mono: true },
            { label: "Buyer GLN", value: order.buyerGln, mono: true },
            { label: "Supplier GLN", value: order.supplierGln, mono: true },
            { label: "Excluding VAT", value: order.totalExcludingTax !== null && order.totalExcludingTax !== undefined ? formatMoneyZar(order.totalExcludingTax) : null },
            { label: "VAT", value: order.totalTax !== null && order.totalTax !== undefined ? formatMoneyZar(order.totalTax) : null },
            { label: "Order header ID", value: order.orderHeaderId, mono: true },
          ]}
        />
      }
      activity={
        <Timeline
          label="Purchase order activity"
          entries={[
            { id: "last", time: formatDateTime(order.lastSeenAt), dateTime: order.lastSeenAt, title: "Last received from Shoprite", detail: order.sourceEndpoint },
            ...(order.shopriteLastUpdatedAt
              ? [{ id: "updated", time: formatDateTime(order.shopriteLastUpdatedAt), dateTime: order.shopriteLastUpdatedAt, title: "Changed on the Shoprite portal" }]
              : []),
            { id: "first", time: formatDateTime(order.firstSeenAt), dateTime: order.firstSeenAt, title: "First received from Shoprite" },
            ...(order.shopriteCreatedAt
              ? [{ id: "created", time: formatDateTime(order.shopriteCreatedAt), dateTime: order.shopriteCreatedAt, title: "Created on the Shoprite portal" }]
              : []),
          ]}
        />
      }
      sections={
        <TabNav
          label="Purchase order sections"
          tabs={[
            { label: "Lines", href: base, current: tab === "lines", count: order.lines.length },
            { label: "Linked invoices", href: `${base}?tab=invoices`, current: tab === "invoices", count: order.linkedInvoiceCandidates.length },
            ...(isAdmin ? [{ label: "Raw payload", href: `${base}?tab=raw`, current: tab === "raw" }] : []),
          ]}
        />
      }
    >
      {tab === "lines" ? (
        <Panel flush title="Lines">
          <DataTable caption="Purchase order lines" columns={lineColumns} rows={order.lines} rowKey={(line) => line.id} empty={<EmptyState title="This order has no lines" />} />
        </Panel>
      ) : null}
      {tab === "invoices" ? (
        <Panel flush title="Linked invoices">
          <DataTable
            caption="Invoices linked to this purchase order"
            columns={invoiceColumns}
            rows={order.linkedInvoiceCandidates}
            rowKey={(row) => row.id}
            rowHref={(row) => `/shoprite/invoices/${row.id}`}
            empty={<EmptyState title="No invoices use this PO number yet" description="An invoice links here when its customer order number matches this PO." />}
          />
        </Panel>
      ) : null}
      {tab === "raw" ? (
        <Panel title="Raw payload" description={`As received from Shoprite. Payload hash ${order.payloadHash ?? "not stored"}.`}>
          <CodeBlock label="Raw purchase order JSON" code={order.rawOrderJson} language="json" empty="The raw payload was not stored for this order." />
        </Panel>
      ) : null}
    </RecordPage>
  );
}

async function loadPurchaseOrder(id: string) {
  try {
    return await getPurchaseOrder(id);
  } catch {
    notFound();
  }
}
