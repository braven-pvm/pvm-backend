import Link from "next/link";
import { requireWorkbenchUser } from "../src/auth/session";

export default async function HomePage() {
  await requireWorkbenchUser("/");

  return (
    <main className="page-shell">
      <section className="page-heading">
        <div>
          <h1>Shoprite invoice operations</h1>
          <p>
            Review finalized Acumatica invoices before manual submission to the
            Shoprite VendorInvoice service.
          </p>
        </div>
        <Link className="button" href="/invoices">
          Open invoice queue
        </Link>
      </section>

      <section className="summary-grid" aria-label="Workbench areas">
        <div className="summary-panel">
          <span className="summary-label">MVP mode</span>
          <strong>Manual select and submit</strong>
          <p>
            The first slice keeps submission human-controlled while preserving
            the path to automated finalization-triggered submission later.
          </p>
        </div>
        <div className="summary-panel">
          <span className="summary-label">Controls</span>
          <strong>Admin access required</strong>
          <p>
            Operators need full visibility into candidates, validation status,
            retries, dead letters, and submission attempts.
          </p>
        </div>
        <div className="summary-panel">
          <span className="summary-label">Environments</span>
          <strong>Staging first</strong>
          <p>
            Acumatica and Shoprite staging environments remain first-class
            validation targets before production credentials are used.
          </p>
        </div>
      </section>
    </main>
  );
}
