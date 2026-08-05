import Link from "next/link";
import { refreshPurchaseOrdersAction } from "../actions";
import { getPurchaseOrderFreshness, getPurchaseOrders } from "../../src/api/client";
import { hasAnyRole, requireWorkbenchUser } from "../../src/auth/session";

export const dynamic = "force-dynamic";

export default async function PurchaseOrdersPage() {
  const user = await requireWorkbenchUser("/purchase-orders");
  const [purchaseOrders, freshness] = await Promise.all([
    getPurchaseOrders(),
    getPurchaseOrderFreshness(),
  ]);
  const canWrite = hasAnyRole(user, ["Admin", "Operator"]);
  const normalCount = purchaseOrders.filter((order) => order.orderTypeCode === "220").length;
  const allocationCount = purchaseOrders.filter((order) => order.orderTypeCode === "258").length;

  return (
    <main className="page-shell">
      <section className="page-heading">
        <div>
          <h1>Shoprite PO Inbox</h1>
          <p>
            Refresh and inspect Shoprite QA purchase orders used as the delivery
            location and item context for invoice submission.
          </p>
        </div>
        {canWrite ? (
          <form action={refreshPurchaseOrdersAction}>
            <button className="button" type="submit">
              Refresh POs
            </button>
          </form>
        ) : (
          <button className="button" type="button" disabled>
            Read-only
          </button>
        )}
      </section>

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
          <span>PO data freshness</span>
          <strong className="metric-text">{freshness.status}</strong>
          <small>
            {freshness.lastSuccessfulRefreshAt
              ? new Date(freshness.lastSuccessfulRefreshAt).toLocaleString()
              : "No successful refresh"}
          </small>
        </div>
      </section>

      <section className="compact-stats" aria-label="Purchase order type counts">
        <span>Normal: <strong>{normalCount}</strong></span>
        <span>Allocation: <strong>{allocationCount}</strong></span>
        <span>Stale threshold: <strong>{freshness.staleAfterMinutes} min</strong></span>
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
              Refresh pulls the current QA VendorOrder batch into the local
              inbox for invoice matching.
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

