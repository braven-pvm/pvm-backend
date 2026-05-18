import { getInvoiceCandidates } from "../../src/api/client";

export const dynamic = "force-dynamic";

export default async function InvoiceCandidatesPage() {
  const candidates = await getInvoiceCandidates();
  const candidateCount = candidates.length;

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
        <form>
          <button className="button" formAction="/api/refresh" disabled>
            Refresh queue
          </button>
        </form>
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
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {candidates.map((candidate, index) => (
                <tr key={candidate.id ?? index}>
                  <td>{candidate.invoiceNumber ?? candidate.id ?? "Unknown"}</td>
                  <td>{candidate.customerName ?? "Unmapped"}</td>
                  <td>{candidate.status ?? "Pending validation"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </main>
  );
}
