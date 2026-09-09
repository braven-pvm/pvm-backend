import { hasAnyRole, requireWorkbenchUser } from "../../src/auth/session";
import { NedbankUploadForm } from "./nedbank-upload-form";

export const dynamic = "force-dynamic";

type BankingPageProps = {
  searchParams: Promise<{ importError?: string; importStatus?: string }>;
};

export default async function BankingPage({ searchParams }: BankingPageProps) {
  const user = await requireWorkbenchUser("/banking");
  const canImport = hasAnyRole(user, ["Admin", "Operator"]);
  const params = await searchParams;

  return (
    <main className="page-shell">
      <section className="page-heading">
        <div>
          <h1>Bank import</h1>
          <p>
            Upload a Nedbank statement in OFX format. The import cleans the file, renumbers every
            transaction, and writes only the new lines into Acumatica.
          </p>
        </div>
      </section>

      {params.importError ? (
        <div className="alert-banner alert-error" role="alert">
          <strong>Statement not imported</strong>
          <span>{params.importError}</span>
        </div>
      ) : null}
      {params.importStatus ? (
        <div className="alert-banner alert-success" role="status">
          <strong>Import complete</strong>
          <span>{params.importStatus}</span>
        </div>
      ) : null}

      <section className="detail-panel">
        <span className="section-kicker">Nedbank</span>
        <h2>Upload an OFX statement</h2>
        <p>
          Download the statement from Nedbank online banking in OFX format, then upload it here.
          Do not import the same statement directly into Acumatica.
        </p>
        {canImport ? (
          <NedbankUploadForm />
        ) : (
          <p className="empty-state">
            You need the Operator role or the Admin role to import a statement.
          </p>
        )}
      </section>

      <section className="detail-panel">
        <span className="section-kicker">What the import does</span>
        <h2>Every upload is safe to repeat</h2>
        <ul className="consequence-list">
          <li>The parser removes the zero-amount lines that Nedbank adds as noise.</li>
          <li>Each transaction receives a stable identifier.</li>
          <li>The import drops every line that Acumatica already holds.</li>
          <li>The statement lands on cash account PVMNEDBANK, ready for reconciliation.</li>
        </ul>
      </section>

      <section className="detail-panel">
        <span className="section-kicker">Investec</span>
        <h2>Investec needs no upload</h2>
        <p>
          The Investec feed reads transactions over the bank API on a schedule. It needs no file
          and no person.
        </p>
      </section>
    </main>
  );
}
