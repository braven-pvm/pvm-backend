"use client";

import { useState } from "react";
import { importNedbankStatementAction, previewNedbankStatementAction } from "../../actions";
import type { BankAccountStatus, BankStatementPreview, BankStatementPreviewLine, NedbankImportResult } from "../../../src/api/client";
import { formatDayAndDate, formatMoneyZar, formatStatementDate } from "../../../src/formatters.mjs";
import {
  Button,
  ButtonLink,
  DataTable,
  EmptyState,
  FileDrop,
  FormActions,
  KeyValueList,
  Notice,
  Panel,
  Spinner,
  Stack,
  StatTile,
  StatusBadge,
  TaskPage,
  TileGrid,
  type Column,
} from "../../../src/ui";

type Stage = "start" | "checking" | "check" | "importing" | "done" | "failed";

const steps = ["Choose the statement", "Check what will change", "Done"];

const header = {
  eyebrow: "Banking",
  title: "Import a Nedbank statement",
  description: "The import finds the account, numbers the lines and skips anything already in Acumatica. Nothing is sent until you confirm.",
};

const back = { href: "/banking", label: "Banking" };

type Props = {
  uploadAccount?: BankAccountStatus;
  statusFailed: boolean;
};

const lineColumns: Column<BankStatementPreviewLine & { key: string }>[] = [
  { key: "date", header: "Date", render: (line) => formatDayAndDate(line.date).replace(/^\w+ /, "") },
  { key: "description", header: "Description", render: (line) => line.description },
  { key: "in", header: "Money in", render: (line) => (line.receipt > 0 ? formatMoneyZar(line.receipt) : "—"), align: "end" },
  { key: "out", header: "Money out", render: (line) => (line.disbursement > 0 ? formatMoneyZar(line.disbursement) : "—"), align: "end" },
  {
    key: "will",
    header: "Will",
    render: (line) =>
      line.alreadyImported ? <StatusBadge status="Skipped" label="Skip · already imported" /> : <StatusBadge status="Ready" label="Import" />,
  },
];

export function BankImportFlow({ uploadAccount, statusFailed }: Props) {
  const [stage, setStage] = useState<Stage>("start");
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<BankStatementPreview | null>(null);
  const [result, setResult] = useState<NedbankImportResult | null>(null);
  const [failure, setFailure] = useState("");

  function restart() {
    setStage("start");
    setFile(null);
    setPreview(null);
    setResult(null);
    setFailure("");
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

  const current = stage === "start" || stage === "checking" ? 0 : stage === "done" ? 2 : stage === "failed" ? (preview ? 1 : 0) : 1;

  return (
    <TaskPage back={back} header={header} steps={steps} current={current}>
      {stage === "start" || stage === "checking" ? (
        <Stack gap="m">
          {statusFailed ? (
            <Notice tone="warning" title="Acumatica could not be reached">
              The last import date may be out of date. Try again in a few minutes before you import.
            </Notice>
          ) : null}
          <FileDrop
            title="Drop your statement here"
            hint={`Nedbank calls this an OFX file. It is the one marked “Bank statement (OFX)” on the download screen.${
              uploadAccount?.importedThrough ? ` Choose a statement that starts after ${formatStatementDate(uploadAccount.importedThrough)}.` : ""
            }`}
            accept=".ofx,application/x-ofx,text/plain"
            busy={stage === "checking"}
            onFile={(chosen) => void check(chosen)}
          />
        </Stack>
      ) : null}

      {stage === "failed" ? (
        <Stack gap="m">
          <Notice tone="error" title="The statement was not imported" live>
            <p>{failure}</p>
            <p>Nothing in Acumatica was changed. If this keeps happening, send this wording to Marius with the time you tried.</p>
          </Notice>
          <FormActions>
            {file ? (
              <Button variant="primary" onClick={() => void check(file)}>
                Try again
              </Button>
            ) : null}
            <Button variant="secondary" onClick={restart}>
              Choose a different file
            </Button>
          </FormActions>
        </Stack>
      ) : null}

      {(stage === "check" || stage === "importing") && preview ? (
        <CheckStep preview={preview} fileName={file?.name ?? ""} busy={stage === "importing"} onImport={() => void commit()} onRestart={restart} />
      ) : null}

      {stage === "done" && result ? <DoneStep result={result} onRestart={restart} /> : null}
    </TaskPage>
  );
}

function CheckStep({
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
      <Stack gap="m">
        <Notice tone="success" title="Nothing new to import">
          All {preview.transactionCount} transactions in {fileName} were imported already. Acumatica stays untouched, so no money is
          counted twice.
        </Notice>
        <FormActions>
          <Button variant="primary" onClick={onRestart}>
            Choose a different file
          </Button>
          <ButtonLink href="/banking" variant="tertiary">
            Back to banking
          </ButtonLink>
        </FormActions>
      </Stack>
    );
  }

  const receipts = preview.lines.filter((line) => line.receipt > 0).length;
  const payments = preview.lines.filter((line) => line.disbursement > 0).length;

  return (
    <Stack gap="m">
      <Panel title="Statement" description="Nothing has been sent to Acumatica yet.">
        <KeyValueList
          items={[
            { label: "File", value: fileName },
            { label: "Account", value: `Nedbank ${preview.sourceAccountNumber ?? ""} → ${preview.cashAccount}` },
            { label: "Covers", value: period },
          ]}
        />
      </Panel>

      <TileGrid min={170}>
        <StatTile label="Opening balance" value={formatMoneyZar(preview.openingBalance)} detail={preview.balancesCarryForward ? "Matches your last import" : undefined} />
        <StatTile label="Money in" value={formatMoneyZar(preview.moneyIn)} detail={receipts === 1 ? "1 transaction" : `${receipts} transactions`} />
        <StatTile label="Money out" value={formatMoneyZar(preview.moneyOut)} detail={payments === 1 ? "1 transaction" : `${payments} transactions`} />
        <StatTile label="Closing balance" value={formatMoneyZar(preview.closingBalance)} detail="Matches the statement" />
      </TileGrid>

      {blocked ? (
        <Notice tone="error" title="These days are already in Acumatica">
          Nedbank is imported through {formatStatementDate(preview.importedThrough)}. Importing this statement again would count the
          same money twice. Download a statement that starts after that date.
        </Notice>
      ) : preview.balancesCarryForward ? (
        <Notice tone="success" title="The balances line up">
          This statement starts exactly where your last import ended, and the figures add up to the closing balance.
        </Notice>
      ) : preview.importedThrough ? (
        <Notice tone="warning" title="The opening balance does not match your last import">
          Your last import closed at {formatMoneyZar(preview.importedThroughBalance)}, and this statement opens at{" "}
          {formatMoneyZar(preview.openingBalance)}. A statement may be missing.
        </Notice>
      ) : null}

      {preview.uncoveredDates.length > 0 ? (
        <Notice tone="warning" title={`${preview.uncoveredDates.length === 1 ? "One day is" : `${preview.uncoveredDates.length} days are`} not covered`}>
          No statement covers {preview.uncoveredDates.map(formatDayAndDate).join(", ")}. If the bank recorded nothing on those days,
          carry on.
        </Notice>
      ) : null}

      {preview.alreadyImportedCount > 0 && !blocked ? (
        <Notice tone="warning" title={`${preview.alreadyImportedCount} already imported`}>
          Those lines will be skipped. Only the {preview.newCount} new ones go in.
        </Notice>
      ) : null}

      <Panel
        flush
        title={`The ${preview.transactionCount} ${preview.transactionCount === 1 ? "transaction" : "transactions"}`}
        description={`These go to Acumatica cash account ${preview.cashAccount}. Net change ${formatMoneyZar(preview.moneyIn - preview.moneyOut)}.`}
      >
        <DataTable
          caption="Statement transactions"
          columns={lineColumns}
          rows={preview.lines.map((line, index) => ({ ...line, key: `${line.date}-${index}` }))}
          rowKey={(line) => line.key}
          empty={<EmptyState title="The statement has no transactions" />}
        />
      </Panel>

      <FormActions>
        {busy ? (
          <Spinner label="Importing into Acumatica…" />
        ) : blocked ? (
          <>
            <Button variant="primary" onClick={onRestart}>
              Choose a different file
            </Button>
            <Button variant="tertiary" onClick={onImport}>
              Import it anyway
            </Button>
          </>
        ) : (
          <>
            <Button variant="primary" onClick={onImport}>
              Import {preview.newCount} {preview.newCount === 1 ? "transaction" : "transactions"}
            </Button>
            <Button variant="tertiary" onClick={onRestart}>
              Choose a different file
            </Button>
          </>
        )}
      </FormActions>
    </Stack>
  );
}

function DoneStep({ result, onRestart }: { result: NedbankImportResult; onRestart: () => void }) {
  const nothingNew = result.linesImported === 0;
  return (
    <Stack gap="m">
      {nothingNew ? (
        <Notice tone="success" title="Nothing new to import" live>
          Every transaction in {result.fileName} was already in Acumatica. Acumatica was left untouched, so no money is counted twice.
        </Notice>
      ) : (
        <Notice tone="success" title={`${result.linesImported} ${result.linesImported === 1 ? "transaction" : "transactions"} imported`} live>
          {result.statementReference
            ? `Acumatica statement ${result.statementReference} is ready for you to reconcile.`
            : "The statement is ready for you to reconcile."}
        </Notice>
      )}
      {nothingNew ? null : (
        <Panel title="In Acumatica">
          <KeyValueList
            items={[
              { label: "Statement", value: result.statementReference ?? null, mono: true },
              {
                label: "Transactions",
                value: `${result.linesImported}${result.alreadyImportedCount > 0 ? `, ${result.alreadyImportedCount} were already there` : ", all new"}`,
              },
              { label: "Opening balance", value: formatMoneyZar(result.openingBalance) },
              { label: "Closing balance", value: formatMoneyZar(result.closingBalance) },
            ]}
          />
        </Panel>
      )}
      <FormActions>
        <ButtonLink href="/banking" variant="primary">
          Back to banking
        </ButtonLink>
        <Button variant="secondary" onClick={onRestart}>
          Import another statement
        </Button>
      </FormActions>
    </Stack>
  );
}
