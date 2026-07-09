import Link from "next/link";
import { notFound } from "next/navigation";
import { seedInvoiceFromPurchaseOrderAction } from "../../actions";
import { getPurchaseOrder } from "../../../src/api/client";
import { hasAnyRole, requireWorkbenchUser } from "../../../src/auth/session";
import { formatMoney } from "../../../src/formatters.mjs";

export const dynamic = "force-dynamic";

type PurchaseOrderDetailPageProps = {
  params: Promise<{
    id: string;
  }>;
};

export default async function PurchaseOrderDetailPage({
  params,
}: PurchaseOrderDetailPageProps) {
  const { id } = await params;
  const user = await requireWorkbenchUser(`/purchase-orders/${id}`);
  const order = await loadPurchaseOrder(id);
  const canWrite = hasAnyRole(user, ["Admin", "Operator"]);

  return (
    <main className="page-shell">
      <section className="page-heading">
        <div>
          <Link className="back-link" href="/purchase-orders">
            Back to PO inbox
          </Link>
          <h1>{order.purchaseOrderNumber}</h1>
          <p>
            {order.deliveryLocationName ?? "Unknown location"} ·{" "}
            {order.deliveryGln ?? "No delivery GLN"}
          </p>
        </div>
        <div className="action-row">
          <span className="status-pill">{order.sourceEnvironment}</span>
          {canWrite ? (
            <form action={seedInvoiceFromPurchaseOrderAction}>
              <input name="id" type="hidden" value={order.id} />
              <button className="button" type="submit">
                Seed test invoice
              </button>
            </form>
          ) : null}
        </div>
      </section>

      <section className="detail-grid">
        <div className="detail-panel">
          <h2>Order context</h2>
          <dl>
            <div>
              <dt>Order header ID</dt>
              <dd>{order.orderHeaderId ?? "-"}</dd>
            </div>
            <div>
              <dt>Order type</dt>
              <dd>
                {order.orderTypeCode ?? "-"} {order.orderTypeLabel ?? ""}
              </dd>
            </div>
            <div>
              <dt>Supplier GLN</dt>
              <dd>{order.supplierGln ?? "-"}</dd>
            </div>
            <div>
              <dt>Buyer GLN</dt>
              <dd>{order.buyerGln ?? "-"}</dd>
            </div>
            <div>
              <dt>Payload hash</dt>
              <dd>{order.payloadHash ?? "-"}</dd>
            </div>
          </dl>
        </div>

        <div className="detail-panel">
          <h2>Delivery location</h2>
          <dl>
            <div>
              <dt>Location code</dt>
              <dd>{order.deliveryLocationCode ?? "-"}</dd>
            </div>
            <div>
              <dt>Location name</dt>
              <dd>{order.deliveryLocationName ?? "-"}</dd>
            </div>
            <div>
              <dt>Delivery GLN</dt>
              <dd>{order.deliveryGln ?? "-"}</dd>
            </div>
            <div>
              <dt>Source field</dt>
              <dd>{order.deliveryLocationSource}</dd>
            </div>
          </dl>
        </div>
      </section>

      <section className="detail-grid">
        <div className="detail-panel">
          <h2>Totals</h2>
          <dl>
            <div>
              <dt>Excluding tax</dt>
              <dd>{formatMoney(order.currencyCode, order.totalExcludingTax)}</dd>
            </div>
            <div>
              <dt>Tax</dt>
              <dd>{formatMoney(order.currencyCode, order.totalTax)}</dd>
            </div>
            <div>
              <dt>Including tax</dt>
              <dd>{formatMoney(order.currencyCode, order.totalIncludingTax)}</dd>
            </div>
          </dl>
        </div>

        <div className="detail-panel">
          <h2>Refresh metadata</h2>
          <dl>
            <div>
              <dt>First seen</dt>
              <dd>{new Date(order.firstSeenAt).toLocaleString()}</dd>
            </div>
            <div>
              <dt>Last seen</dt>
              <dd>{new Date(order.lastSeenAt).toLocaleString()}</dd>
            </div>
            <div>
              <dt>Source endpoint</dt>
              <dd>{order.sourceEndpoint}</dd>
            </div>
          </dl>
        </div>
      </section>

      <section className="table-panel detail-section">
        <div className="table-toolbar">
          <h2>PO lines</h2>
          <span>{order.lines.length} lines</span>
        </div>
        <table>
          <thead>
            <tr>
              <th>Line</th>
              <th>GTIN</th>
              <th>Buyer item</th>
              <th>Description</th>
              <th>Quantity</th>
              <th>Net price</th>
            </tr>
          </thead>
          <tbody>
            {order.lines.map((line) => (
              <tr key={line.id}>
                <td data-label="Line">{line.lineNumber}</td>
                <td data-label="GTIN">{line.gtin ?? "-"}</td>
                <td data-label="Buyer item">
                  {line.buyerItemId ?? "-"}
                  <span>{line.buyerItemDescription ?? ""}</span>
                </td>
                <td data-label="Description">{line.description ?? "-"}</td>
                <td data-label="Quantity">
                  {line.requestedQuantity ?? "-"} {line.measurementUnitCode ?? ""}
                </td>
                <td data-label="Net price">
                  {formatMoney(order.currencyCode, line.netPrice)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <section className="table-panel detail-section">
        <div className="table-toolbar">
          <h2>Linked invoice candidates</h2>
          <span>{order.linkedInvoiceCandidates.length} records</span>
        </div>
        {order.linkedInvoiceCandidates.length === 0 ? (
          <div className="empty-state">
            <strong>No linked invoice candidates</strong>
            <p>
              Invoice candidates link here after refresh or revalidation finds
              this PO number.
            </p>
          </div>
        ) : (
          <table>
            <thead>
              <tr>
                <th>Invoice</th>
                <th>Customer</th>
                <th>Status</th>
                <th>Updated</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {order.linkedInvoiceCandidates.map((candidate) => (
                <tr key={candidate.id}>
                  <td data-label="Invoice">{candidate.invoiceNumber}</td>
                  <td data-label="Customer">{candidate.customerAccount}</td>
                  <td data-label="Status">
                    <span className="status-pill">{candidate.status}</span>
                  </td>
                  <td data-label="Updated">
                    {new Date(candidate.updatedAt).toLocaleString()}
                  </td>
                  <td className="table-action" data-label="Action">
                    <Link href={`/invoices/${candidate.id}`}>Open</Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      <section className="table-panel detail-section">
        <div className="table-toolbar">
          <h2>Raw PO payload</h2>
          <span>{order.rawOrderJson ? "stored" : "not available"}</span>
        </div>
        <pre className="xml-preview">{formatJson(order.rawOrderJson)}</pre>
      </section>
    </main>
  );
}

async function loadPurchaseOrder(id: string) {
  try {
    return await getPurchaseOrder(id);
  } catch {
    notFound();
  }
}

function formatJson(value: string | null | undefined) {
  if (!value) {
    return "No raw payload available.";
  }

  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}

