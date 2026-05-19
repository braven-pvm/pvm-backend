import Link from "next/link";
import { refreshCandidatesAction } from "../actions";
import { getInvoiceCandidates } from "../../src/api/client";
import { hasAnyRole, requireWorkbenchUser } from "../../src/auth/session";

export const dynamic = "force-dynamic";

export default async function InvoiceCandidatesPage() {
  const user = await requireWorkbenchUser();
  const candidates = await getInvoiceCandidates();
  const candidateCount = candidates.length;
  const canWrite = hasAnyRole(user, ["Admin", "Operator"]);

  return (
    <main className="page-shell">
      <section className="page-heading">
        <div>
          <h1>Invoice Submission Workbench</h1>
          <p>
            Monitor finalized Acumatica invoice candidates prepared for
            Shoprite submission.
          </p>
        </div>
        {canWrite ? (
          <form action={refreshCandidatesAction}>
            <button className="button" type="submit">
              Refresh queue
            </button>
          </form>
        ) : (
          <button className="button" type="button" disabled>
            Read-only
          </button>
        )}
      </section>

      <section className="metric-strip" aria-label="Invoice queue summary">
        <div>
          <span>Candidate invoices</span>
          <strong>{candidateCount}</strong>
        </div>
        <div>
          <span>Ready for submission</span>
          <strong>0</strong>
        </div>
        <div>
          <span>Needs review</span>
          <strong>0</strong>
        </div>
      </section>

      <section className="table-panel" aria-label="Invoice candidates">
        <div className="table-toolbar">
          <h2>Invoice candidates</h2>
          <span>{candidateCount} records</span>
        </div>
        {candidateCount === 0 ? (
          <div className="empty-state">
            <strong>No invoice candidates loaded</strong>
            <p>
              Task 12 will wire refresh, candidate details, XML preview, and
              manual submission against sanitized fixture data.
            </p>
          </div>
        ) : (
          <table>
            <thead>
              <tr>
                <th>Invoice</th>
                <th>Customer</th>
                <th>DC</th>
                <th>Status</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {candidates.map((candidate) => (
                <tr key={candidate.id}>
                  <td data-label="Invoice">{candidate.invoiceNumber}</td>
                  <td data-label="Customer">{candidate.customerAccount}</td>
                  <td data-label="DC">
                    {candidate.customerLocation ?? candidate.storeDcGln ?? "Unmapped"}
                  </td>
                  <td data-label="Status">
                    <span className="status-pill">{candidate.status}</span>
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
    </main>
  );
}
