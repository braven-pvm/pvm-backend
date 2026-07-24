import Link from "next/link";
import { notFound } from "next/navigation";
import {
  saveInvoiceLineMappingAction,
  submitInvoiceAction,
} from "../../actions";
import { getInvoiceCandidate } from "../../../src/api/client";
import { hasAnyRole, requireWorkbenchUser } from "../../../src/auth/session";

export const dynamic = "force-dynamic";

type InvoiceDetailPageProps = {
  params: Promise<{
    id: string;
  }>;
};

export default async function InvoiceDetailPage({
  params,
}: InvoiceDetailPageProps) {
  const { id } = await params;
  const user = await requireWorkbenchUser(`/invoices/${id}`);
  const candidate = await loadCandidate(id);
  const invoice = candidate.canonicalInvoice;
  const canWrite = hasAnyRole(user, ["Admin", "Operator"]);
  const canManageMappings = hasAnyRole(user, ["Admin"]);

  return (
    <main className="page-shell">
      <section className="page-heading">
        <div>
          <Link className="back-link" href="/invoices">
            Back to invoices
          </Link>
          <h1>{invoice?.invoiceNumber ?? "Invoice candidate"}</h1>
          <p>
            {invoice?.customerAccount ?? "Unknown customer"} ·{" "}
            {invoice?.customerLocation ?? invoice?.storeDcGln ?? "Unmapped DC"}
          </p>
        </div>
        {candidate.canSubmit && canWrite ? (
          <form action={submitInvoiceAction}>
            <input name="id" type="hidden" value={candidate.id} />
            <button className="button" type="submit">
              Submit to Shoprite
            </button>
          </form>
        ) : (
          <button className="button" type="button" disabled>
            {canWrite ? "Submission blocked" : "Read-only"}
          </button>
        )}
      </section>

      <section className="detail-grid">
        <div className="detail-panel">
          <h2>Invoice identifiers</h2>
          <dl>
            <div>
              <dt>Acumatica ID</dt>
              <dd>{invoice?.acumaticaInvoiceId ?? "-"}</dd>
            </div>
            <div>
              <dt>Shoprite PO</dt>
              <dd>{invoice?.shopritePurchaseOrderNumber ?? "-"}</dd>
            </div>
            <div>
              <dt>Supplier GLN</dt>
              <dd>{invoice?.supplierGln ?? "-"}</dd>
            </div>
            <div>
              <dt>Store/DC GLN</dt>
              <dd>{invoice?.storeDcGln ?? "-"}</dd>
            </div>
            <div>
              <dt>Status</dt>
              <dd>
                <span className="status-pill">{candidate.status}</span>
              </dd>
            </div>
          </dl>
        </div>

        <div className="detail-panel">
          <h2>Totals</h2>
          <dl>
            <div>
              <dt>Excluding VAT</dt>
              <dd>{formatMoney(invoice?.totalExcludingTax)}</dd>
            </div>
            <div>
              <dt>VAT</dt>
              <dd>{formatMoney(invoice?.totalTax)}</dd>
            </div>
            <div>
              <dt>Including VAT</dt>
              <dd>{formatMoney(invoice?.totalIncludingTax)}</dd>
            </div>
          </dl>
        </div>
      </section>

      <section className="detail-grid">
        <div className="detail-panel">
          <h2>Matched Shoprite PO</h2>
          {candidate.matchedPurchaseOrder ? (
            <dl>
              <div>
                <dt>PO number</dt>
                <dd>
                  <Link href={`/purchase-orders/${candidate.matchedPurchaseOrder.id}`}>
                    {candidate.matchedPurchaseOrder.purchaseOrderNumber}
                  </Link>
                </dd>
              </div>
              <div>
                <dt>Order type</dt>
                <dd>
                  {candidate.matchedPurchaseOrder.orderTypeCode ?? "-"}{" "}
                  {candidate.matchedPurchaseOrder.orderTypeLabel ?? ""}
                </dd>
              </div>
              <div>
                <dt>Delivery location</dt>
                <dd>
                  {candidate.matchedPurchaseOrder.deliveryLocationCode ?? "-"} ·{" "}
                  {candidate.matchedPurchaseOrder.deliveryLocationName ?? "-"}
                </dd>
              </div>
              <div>
                <dt>Delivery GLN</dt>
                <dd>{candidate.matchedPurchaseOrder.deliveryGln ?? "-"}</dd>
              </div>
              <div>
                <dt>Location source</dt>
                <dd>{candidate.matchedPurchaseOrder.deliveryLocationSource}</dd>
              </div>
            </dl>
          ) : (
            <div className="empty-state compact">
              <strong>No local PO match</strong>
              <p>Refresh the Shoprite PO inbox, then revalidate this invoice.</p>
            </div>
          )}
        </div>

        <div className="detail-panel">
          <h2>PO line context</h2>
          <dl>
            <div>
              <dt>PO lines</dt>
              <dd>{candidate.matchedPurchaseOrder?.lineCount ?? 0}</dd>
            </div>
            <div>
              <dt>Candidate lines</dt>
              <dd>{invoice?.lines.length ?? 0}</dd>
            </div>
          </dl>
        </div>
      </section>

      {canManageMappings &&
      candidate.matchedPurchaseOrder &&
      invoice?.lines.length ? (
        <section className="table-panel detail-section">
          <div className="table-toolbar">
            <h2>Line mappings</h2>
            <span>Admin managed</span>
          </div>
          <table className="mapping-table">
            <thead>
              <tr>
                <th>Acumatica line</th>
                <th>Current mapping</th>
                <th>Shoprite PO line</th>
                <th>Shoprite UOM</th>
                <th aria-label="Action" />
              </tr>
            </thead>
            <tbody>
              {invoice.lines.map((line) => {
                const formId = `line-mapping-${line.lineNumber}`;
                const selectedPoLine =
                  candidate.matchedPurchaseOrder?.lines.find(
                    (poLine) => poLine.gtin === line.gtin,
                  ) ??
                  (candidate.matchedPurchaseOrder?.lines.length === 1
                    ? candidate.matchedPurchaseOrder.lines[0]
                    : undefined);

                return (
                  <tr key={line.lineNumber}>
                    <td data-label="Acumatica line">
                      <strong>{line.acumaticaInventoryId}</strong>
                      <span>
                        Line {line.lineNumber} · {line.acumaticaUom}
                      </span>
                    </td>
                    <td data-label="Current mapping">
                      {line.gtin ?? "No GTIN"}
                      <span>
                        {line.shopriteUom
                          ? `${line.shopriteUom}${line.isShopriteUomVerified ? " · verified" : " · unverified"}`
                          : "No Shoprite UOM"}
                      </span>
                    </td>
                    <td data-label="Shoprite PO line">
                      <select
                        defaultValue={selectedPoLine?.id ?? ""}
                        form={formId}
                        name="purchaseOrderLineId"
                        required
                      >
                        <option value="">Select PO line</option>
                        {candidate.matchedPurchaseOrder?.lines.map((poLine) => (
                          <option key={poLine.id} value={poLine.id}>
                            {poLine.lineNumber} ·{" "}
                            {poLine.buyerItemId ?? "No buyer item"} ·{" "}
                            {poLine.gtin ?? "No GTIN"} ·{" "}
                            {poLine.buyerItemDescription ??
                              poLine.description ??
                              "No description"}
                          </option>
                        ))}
                      </select>
                    </td>
                    <td data-label="Shoprite UOM">
                      <select
                        defaultValue={line.shopriteUom ?? ""}
                        form={formId}
                        name="shopriteUom"
                        required
                      >
                        <option value="">Select UOM</option>
                        <option value="EA">EA · each</option>
                        <option value="CA">CA · case</option>
                        <option value="CS">CS · case</option>
                        <option value="KG">KG · kilogram</option>
                      </select>
                    </td>
                    <td className="table-action" data-label="Action">
                      <form action={saveInvoiceLineMappingAction} id={formId}>
                        <input name="id" type="hidden" value={candidate.id} />
                        <input
                          name="lineNumber"
                          type="hidden"
                          value={line.lineNumber}
                        />
                        <button className="button secondary" type="submit">
                          Save mapping
                        </button>
                      </form>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </section>
      ) : null}

      <section className="table-panel detail-section">
        <div className="table-toolbar">
          <h2>Validation</h2>
          <span>{candidate.validation.issues.length} issues</span>
        </div>
        {candidate.validation.issues.length === 0 ? (
          <div className="empty-state">
            <strong>No validation issues</strong>
            <p>This candidate is structurally ready for Shoprite submission.</p>
          </div>
        ) : (
          <table>
            <thead>
              <tr>
                <th>Severity</th>
                <th>Code</th>
                <th>Message</th>
                <th>Fix location</th>
              </tr>
            </thead>
            <tbody>
              {candidate.validation.issues.map((issue) => (
                <tr key={issue.code}>
                  <td data-label="Severity">{issue.severity}</td>
                  <td data-label="Code">{issue.code}</td>
                  <td data-label="Message">{issue.message}</td>
                  <td data-label="Fix location">{issue.fixLocation}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      <section className="table-panel detail-section">
        <div className="table-toolbar">
          <h2>Generated XML</h2>
          <span>{candidate.generatedXml ? "preview" : "not available"}</span>
        </div>
        <pre className="xml-preview">{candidate.generatedXml ?? "No XML available."}</pre>
      </section>

      <section className="table-panel detail-section">
        <div className="table-toolbar">
          <h2>Attempt history</h2>
          <span>{candidate.attempts.length} attempts</span>
        </div>
        {candidate.attempts.length === 0 ? (
          <div className="empty-state">
            <strong>No submission attempts yet</strong>
            <p>Manual submission attempts will appear here.</p>
          </div>
        ) : (
          <table>
            <thead>
              <tr>
                <th>Status</th>
                <th>Mode</th>
                <th>User</th>
                <th>HTTP</th>
                <th>Result</th>
                <th>Created</th>
              </tr>
            </thead>
            <tbody>
              {candidate.attempts.map((attempt) => (
                <tr key={attempt.id}>
                  <td data-label="Status">{attempt.status}</td>
                  <td data-label="Mode">{attempt.initiationMode}</td>
                  <td data-label="User">{attempt.initiatedBy}</td>
                  <td data-label="HTTP">{attempt.responseStatusCode ?? "-"}</td>
                  <td className="attempt-result" data-label="Result">
                    {attempt.failureClassification ?? attempt.errorMessage ?? "-"}
                  </td>
                  <td data-label="Created">
                    {new Date(attempt.createdAt).toLocaleString()}
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

async function loadCandidate(id: string) {
  try {
    return await getInvoiceCandidate(id);
  } catch {
    notFound();
  }
}

function formatMoney(
  money: { currencyCode: string; amount: number } | undefined,
) {
  if (!money) {
    return "-";
  }

  return `${money.currencyCode} ${money.amount.toFixed(2)}`;
}
