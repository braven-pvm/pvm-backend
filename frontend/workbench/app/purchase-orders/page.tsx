import Link from "next/link";
import { getPurchaseOrderFreshness, getPurchaseOrders } from "../../src/api/client";
import { requireWorkbenchUser } from "../../src/auth/session";

export const dynamic = "force-dynamic";

export default async function PurchaseOrdersPage() {
  await requireWorkbenchUser("/purchase-orders");
  const [purchaseOrders, freshness] = await Promise.all([
    getPurchaseOrders(),
    getPurchaseOrderFreshness(),
  ]);
  const normalCount = purchaseOrders.filter((order) => order.orderTypeCode === "220").length;
  const allocationCount = purchaseOrders.filter((order) => order.orderTypeCode === "258").length;

  return (
    <main className="page-shell">
      <section className="page-heading">
        <div>
          <h1>Shoprite PO Inbox</h1>
          <p>
            Shoprite orders received before order reading was switched off. Invoices no longer
            need them: the store GLN and item GTINs come from Shoprite reference data.
          </p>
        </div>
      </section>

      <div className="alert-banner" role="status">
        <strong>Reading Shoprite orders is switched off</strong>
        <span>
          Shoprite marks an order as downloaded the moment anything reads it, which removes it
          from the people who work orders on the Shoprite portal. This list no longer refreshes.
        </span>
      </div>

      <section className="metric-strip" aria-label="Purchase order summary">
        <div>
          <span>Purchase orders</span>
          <strong>{purchaseOrders.length}</strong>
        </div>
        <div>
          <span>Normal orders</span>
          <strong>{normalCount}</strong>
        </div>
        <div>
          <span>Last received</span>
          <strong className="metric-text">
            {freshness.lastSuccessfulRefreshAt
              ? new Date(freshness.lastSuccessfulRefreshAt).toLocaleDateString()
              : "Never"}
          </strong>
        </div>
      </section>

      <section className="compact-stats" aria-label="Purchase order type counts">
        <span>Normal: <strong>{normalCount}</strong></span>
        <span>Allocation: <strong>{allocationCount}</strong></span>
      </section>

      <section className="table-panel" aria-label="Shoprite purchase orders">
        <div className="table-toolbar">
          <h2>PO inbox</h2>
          <span>{purchaseOrders.length} records</span>
        </div>
        {purchaseOrders.length === 0 ? (
          <div className="empty-state">
            <strong>No Shoprite purchase orders loaded</strong>
            <p>
              Invoices are completed from Shoprite reference data, so this list can stay empty.
            </p>
          </div>
        ) : (
          <table>
            <thead>
              <tr>
                <th>PO</th>
                <th>Type</th>
                <th>Location</th>
                <th>Supplier GLN</th>
                <th>Lines</th>
                <th>Last seen</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {purchaseOrders.map((order) => (
                <tr key={order.id}>
                  <td data-label="PO">{order.purchaseOrderNumber}</td>
                  <td data-label="Type">
                    {order.orderTypeCode ?? "-"}
                    <span>{order.orderTypeLabel ?? "Unknown"}</span>
                  </td>
                  <td data-label="Location">
                    {order.deliveryLocationCode ?? order.deliveryGln ?? "-"}
                    <span>{order.deliveryLocationName ?? order.deliveryLocationSource}</span>
                  </td>
                  <td data-label="Supplier GLN">{order.supplierGln ?? "-"}</td>
                  <td data-label="Lines">{order.lineCount}</td>
                  <td data-label="Last seen">
                    {new Date(order.lastSeenAt).toLocaleString()}
                  </td>
                  <td className="table-action" data-label="Action">
                    <Link href={`/purchase-orders/${order.id}`}>Open</Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </main>
  );
}

