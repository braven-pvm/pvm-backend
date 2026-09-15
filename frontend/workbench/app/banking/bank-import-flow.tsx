"use client";

import { useRef, useState } from "react";
import {
  importNedbankStatementAction,
  previewNedbankStatementAction,
} from "../actions";
import type {
  BankAccountStatus,
  BankStatementPreview,
  NedbankImportResult,
} from "../../src/api/client";
import {
  formatDayAndDate,
  formatMoneyZar,
  formatStatementDate,
} from "../../src/formatters.mjs";

type Stage = "start" | "checking" | "check" | "importing" | "done" | "failed";

type Props = {
  accounts: BankAccountStatus[];
  canImport: boolean;
  statusFailed: boolean;
};

export function BankImportFlow({ accounts, canImport, statusFailed }: Props) {
  const [stage, setStage] = useState<Stage>("start");
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<BankStatementPreview | null>(null);
  const [result, setResult] = useState<NedbankImportResult | null>(null);
  const [failure, setFailure] = useState<string>("");
  const [dragging, setDragging] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const uploadAccount = accounts.find((account) => account.mode === "Upload");

  function restart() {
    setStage("start");
    setFile(null);
    setPreview(null);
    setResult(null);
    setFailure("");
    if (inputRef.current) {
      inputRef.current.value = "";
    }
  }

  async function check(chosen: File) {
    setFile(chosen);
    setStage("checking");
    const body = new FormData();
    body.append("file", chosen);
    const response = await previewNedbankStatementAction(body);
    if (!response.ok) {
      setFailure(response.message);
      setStage("failed");
      return;
    }

    setPreview(response.preview);
    setStage("check");
  }

  async function commit() {
    if (!file) {
      return;
    }

    setStage("importing");
    const body = new FormData();
    body.append("file", file);
    const response = await importNedbankStatementAction(body);
    if (!response.ok) {
      setFailure(response.message);
      setStage("failed");
      return;
    }

    setResult(response.result);
    setStage("done");
  }

  if (stage === "failed") {
    return (
      <Shell heading="The statement was not imported" lead="Nothing in Acumatica was changed.">
        <section className="bank-panel">
          <div className="bank-file-id">
            <Chip tone="stop">Not imported</Chip>
            <span className="bank-file-name">{file?.name ?? "Your statement"}</span>
          </div>
          <div className="bank-panel-body">
            <Callout tone="stop" title="Acumatica did not accept this statement">
              {failure}
            </Callout>
            <p className="bank-note">
              If this keeps happening, send the wording above to Marius with the time you tried.
            </p>
          </div>
        </section>
        <div className="bank-actions">
          {file ? (
            <button className="bank-btn bank-btn-primary" type="button" onClick={() => check(file)}>
              Try again
            </button>
          ) : null}
          <button className="bank-btn bank-btn-secondary" type="button" onClick={restart}>
            Choose a different file
          </button>
        </div>
      </Shell>
    );
  }

  if (stage === "done" && result) {
    const nothingNew = result.linesImported === 0;
    return (
      <Shell
        heading={
          nothingNew
            ? "Nothing new to import"
            : `${result.linesImported} ${result.linesImported === 1 ? "transaction" : "transactions"} imported`
        }
        lead={
          nothingNew
            ? "Every transaction in this file was already in Acumatica."
            : result.statementReference
              ? `Acumatica statement ${result.statementReference} is ready for you to reconcile.`
              : "The statement is ready for you to reconcile."
        }
      >
        <section className="bank-panel">
          <div className="bank-file-id">
            <Chip tone={nothingNew ? "quiet" : "ok"}>{nothingNew ? "No change" : "Imported"}</Chip>
            <span className="bank-file-name">{result.fileName}</span>
          </div>
          <div className="bank-panel-body">
            {nothingNew ? (
              <Callout tone="ok" title="Nothing was changed">
                Acumatica was left untouched, so no money is counted twice.
              </Callout>
            ) : (
              <dl className="bank-figures">
                <Figure label="Statement" value={result.statementReference ?? "—"} note="In Acumatica" />
                <Figure
                  label="Transactions"
                  value={String(result.linesImported)}
                  note={
                    result.alreadyImportedCount > 0
                      ? `${result.alreadyImportedCount} were already there`
                      : "All were new"
                  }
                />
                <Figure label="Opening balance" value={formatMoneyZar(result.openingBalance)} />
                <Figure label="Closing balance" value={formatMoneyZar(result.closingBalance)} />
              </dl>
            )}
          </div>
        </section>
        <div className="bank-actions">
          <button className="bank-btn bank-btn-primary" type="button" onClick={restart}>
            Import another statement
          </button>
        </div>
      </Shell>
    );
  }

  if ((stage === "check" || stage === "importing") && preview) {
    return (
      <CheckScreen
        preview={preview}
        fileName={file?.name ?? ""}
        busy={stage === "importing"}
        onImport={commit}
        onRestart={restart}
      />
    );
  }

  return (
    <Shell heading="Bank import" lead="Bring your bank statements into Acumatica for reconciliation.">
      {statusFailed ? (
        <Callout tone="warn" title="Acumatica could not be reached">
          The dates below may be out of date. Try again in a few minutes before you import.
        </Callout>
      ) : null}

      <div className="bank-accounts">
        {accounts.map((account) => (
          <AccountCard key={account.bank} account={account} />
        ))}
      </div>

      <section className="bank-block">
        <h2>Upload a Nedbank statement</h2>
        <p className="bank-block-sub">
          Download the statement from Nedbank online banking, then drop the file here.
          {uploadAccount?.importedThrough
            ? ` Choose a statement that starts after ${formatStatementDate(uploadAccount.importedThrough)}.`
            : ""}
        </p>

        {canImport ? (
          <div
            className={`bank-drop${dragging ? " is-dragging" : ""}`}
            onDragOver={(event) => {
              event.preventDefault();
              setDragging(true);
            }}
            onDragLeave={() => setDragging(false)}
            onDrop={(event) => {
              event.preventDefault();
              setDragging(false);
              const dropped = event.dataTransfer.files?.[0];
              if (dropped) {
                void check(dropped);
              }
            }}
          >
            <p className="bank-drop-title">Drop your statement here</p>
            <p className="bank-drop-hint">
              Nedbank calls this an OFX file. It is the one marked “Bank statement (OFX)” on the
              download screen.
            </p>
            <label className="bank-btn bank-btn-primary" htmlFor="statement-file">
              {stage === "checking" ? "Reading the file" : "Choose a file"}
            </label>
            <input
              accept=".ofx,application/x-ofx,text/plain"
              className="bank-file-input"
              id="statement-file"
              name="file"
              onChange={(event) => {
                const chosen = event.target.files?.[0];
                if (chosen) {
                  void check(chosen);
                }
              }}
              ref={inputRef}
              type="file"
            />
          </div>
        ) : (
          <p className="bank-empty">
            You need the Operator role or the Admin role to import a statement.
          </p>
        )}
      </section>

      {uploadAccount && uploadAccount.recentStatements.length > 0 ? (
        <section className="bank-block">
          <h2>Recent imports</h2>
          <p className="bank-block-sub">
            Check here before you upload, so the same days are never imported twice.
          </p>
          <div className="bank-panel">
            <ul className="bank-history">
              {uploadAccount.recentStatements.map((statement) => (
                <li key={statement.referenceNbr}>
                  <span className="bank-history-when">
                    {formatStatementDate(statement.endBalanceDate)}
                  </span>
                  <span className="bank-history-what">
                    {statement.lineCount}{" "}
                    {statement.lineCount === 1 ? "transaction" : "transactions"} · closing{" "}
                    {formatMoneyZar(statement.endingBalance)}
                  </span>
                  <span className="bank-history-ref">Statement {statement.referenceNbr}</span>
                </li>
              ))}
            </ul>
          </div>
        </section>
      ) : null}
    </Shell>
  );
}

function CheckScreen({
  preview,
  fileName,
  busy,
  onImport,
  onRestart,
}: {
  preview: BankStatementPreview;
  fileName: string;
  busy: boolean;
  onImport: () => void;
  onRestart: () => void;
}) {
  const blocked = preview.overlapsImportedPeriod;
  const period =
    preview.periodStart === preview.periodEnd
      ? formatDayAndDate(preview.periodStart)
      : `${formatStatementDate(preview.periodStart)} to ${formatStatementDate(preview.periodEnd)}`;

  if (preview.nothingToImport) {
    return (
      <Shell
        heading="Nothing new to import"
        lead="Every transaction in this file is already in Acumatica."
      >
        <section className="bank-panel">
          <div className="bank-file-id">
            <Chip tone="quiet">No change</Chip>
            <span className="bank-file-name">{fileName}</span>
          </div>
          <div className="bank-panel-body">
            <Callout tone="ok" title="Nothing would change">
              All {preview.transactionCount} transactions were imported already. Acumatica would be
              left untouched, so no money is counted twice.
            </Callout>
          </div>
        </section>
        <div className="bank-actions">
          <button className="bank-btn bank-btn-primary" type="button" onClick={onRestart}>
            Back to bank import
          </button>
        </div>
      </Shell>
    );
  }

  return (
    <Shell heading="Check this statement" lead="Nothing has been sent to Acumatica yet.">
      <section className="bank-panel">
        <div className="bank-file-id">
          <Chip tone={blocked ? "stop" : "ok"}>
            {blocked ? "Already imported" : `Nedbank account ${preview.sourceAccountNumber ?? ""}`}
          </Chip>
          <span className="bank-file-name">{fileName}</span>
          <span className="bank-file-meta">{period}</span>
        </div>
        <div className="bank-panel-body">
          <dl className="bank-figures">
            <Figure
              label="Opening balance"
              value={formatMoneyZar(preview.openingBalance)}
              note={preview.balancesCarryForward ? "Matches your last import" : undefined}
            />
            <Figure
              label="Money in"
              value={formatMoneyZar(preview.moneyIn)}
              note={`${preview.lines.filter((line) => line.receipt > 0).length} transactions`}
            />
            <Figure
              label="Money out"
              value={formatMoneyZar(preview.moneyOut)}
              note={`${preview.lines.filter((line) => line.disbursement > 0).length} transactions`}
            />
            <Figure
              label="Closing balance"
              value={formatMoneyZar(preview.closingBalance)}
              note="Matches the statement"
            />
          </dl>

          {blocked ? (
            <Callout tone="stop" title="These days are already in Acumatica">
              Nedbank is imported through {formatStatementDate(preview.importedThrough)}. Importing
              this statement again would count the same money twice. Download a statement that
              starts after that date.
            </Callout>
          ) : preview.balancesCarryForward ? (
            <Callout tone="ok" title="The balances line up">
              This statement starts exactly where your last import ended, and the figures add up to
              the closing balance.
            </Callout>
          ) : preview.importedThrough ? (
            <Callout tone="warn" title="The opening balance does not match your last import">
              Your last import closed at {formatMoneyZar(preview.importedThroughBalance)}, and this
              statement opens at {formatMoneyZar(preview.openingBalance)}. A statement may be
              missing.
            </Callout>
          ) : null}

          {preview.uncoveredDates.length > 0 ? (
            <Callout
              tone="warn"
              title={`${preview.uncoveredDates.length === 1 ? "One day is" : `${preview.uncoveredDates.length} days are`} not covered`}
            >
              No statement covers {preview.uncoveredDates.map(formatDayAndDate).join(", ")}. If the
              bank recorded nothing on those days, carry on.
            </Callout>
          ) : null}

          {preview.alreadyImportedCount > 0 && !blocked ? (
            <Callout tone="warn" title={`${preview.alreadyImportedCount} already imported`}>
              Those lines will be skipped. Only the {preview.newCount} new ones go in.
            </Callout>
          ) : null}
        </div>
      </section>

      <section className="bank-block">
        <h2>
          The {preview.transactionCount}{" "}
          {preview.transactionCount === 1 ? "transaction" : "transactions"}
        </h2>
        <p className="bank-block-sub">These go to Acumatica cash account {preview.cashAccount}.</p>
        <div className="bank-panel">
          <div className="bank-table-scroll">
            <table className="bank-table">
              <thead>
                <tr>
                  <th scope="col">Date</th>
                  <th scope="col">Description</th>
                  <th className="bank-num" scope="col">Money in</th>
                  <th className="bank-num" scope="col">Money out</th>
                </tr>
              </thead>
              <tbody>
                {preview.lines.map((line, index) => (
                  <tr key={`${line.date}-${index}`} className={line.alreadyImported ? "is-skipped" : ""}>
                    <td>{formatDayAndDate(line.date).replace(/^\w+ /, "")}</td>
                    <td className="bank-desc">
                      {line.description}
                      {line.alreadyImported ? <span className="bank-skip-tag">Already imported</span> : null}
                    </td>
                    <td className="bank-num bank-in">
                      {line.receipt > 0 ? formatMoneyZar(line.receipt) : "—"}
                    </td>
                    <td className="bank-num">
                      {line.disbursement > 0 ? formatMoneyZar(line.disbursement) : "—"}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="bank-table-foot">
            <span>
              {preview.newCount} to import
              {preview.alreadyImportedCount > 0 ? `, ${preview.alreadyImportedCount} skipped` : ""}
            </span>
            <span>Net change {formatMoneyZar(preview.moneyIn - preview.moneyOut)}</span>
          </div>
        </div>
      </section>

      <div className="bank-actions">
        {blocked ? (
          <>
            <button className="bank-btn bank-btn-primary" type="button" onClick={onRestart}>
              Choose a different file
            </button>
            <button className="bank-btn bank-btn-quiet" type="button" onClick={onImport} disabled={busy}>
              Import it anyway
            </button>
          </>
        ) : (
          <>
            <button className="bank-btn bank-btn-primary" type="button" onClick={onImport} disabled={busy}>
              {busy
                ? "Importing"
                : `Import ${preview.newCount} ${preview.newCount === 1 ? "transaction" : "transactions"}`}
            </button>
            <button className="bank-btn bank-btn-secondary" type="button" onClick={onRestart}>
              Choose a different file
            </button>
          </>
        )}
      </div>
    </Shell>
  );
}

function AccountCard({ account }: { account: BankAccountStatus }) {
  return (
    <article className="bank-account">
      <div className="bank-account-top">
        <h2>{account.bank}</h2>
        <Chip tone={account.mode === "Automatic" ? "ok" : "quiet"}>
          {account.mode === "Automatic" ? "Automatic" : "You upload"}
        </Chip>
      </div>
      {account.available && account.importedThrough ? (
        <>
          <p className="bank-account-through">
            Imported through <strong>{formatStatementDate(account.importedThrough)}</strong>
          </p>
          <p className="bank-account-balance">{formatMoneyZar(account.closingBalance)}</p>
          <p className="bank-account-foot">Closing balance on the last statement</p>
        </>
      ) : (
        <p className="bank-account-foot">
          {account.available ? "No statements imported yet." : "Could not read Acumatica."}
        </p>
      )}
    </article>
  );
}

function Shell({
  heading,
  lead,
  children,
}: {
  heading: string;
  lead: string;
  children: React.ReactNode;
}) {
  return (
    <main className="page-shell bank-page">
      <div className="bank-head">
        <h1>{heading}</h1>
        <p>{lead}</p>
      </div>
      {children}
    </main>
  );
}

function Figure({ label, value, note }: { label: string; value: string; note?: string }) {
  return (
    <div>
      <dt>{label}</dt>
      <dd>
        {value}
        {note ? <small>{note}</small> : null}
      </dd>
    </div>
  );
}

function Chip({ tone, children }: { tone: "ok" | "warn" | "stop" | "quiet"; children: React.ReactNode }) {
  return <span className={`bank-chip bank-chip-${tone}`}>{children}</span>;
}

function Callout({
  tone,
  title,
  children,
}: {
  tone: "ok" | "warn" | "stop";
  title: string;
  children: React.ReactNode;
}) {
  return (
    <div className={`bank-callout bank-callout-${tone}`} role={tone === "stop" ? "alert" : "status"}>
      <strong>{title}</strong>
      <span>{children}</span>
    </div>
  );
}
