"use client";

import { useState } from "react";
import { importSellOutFileAction, previewSellOutFileAction } from "../../actions";
import { sellOutSources, type SellOutImportResult, type SellOutIssue } from "../../../src/api/sell-out";
import { describeMonths, describeSellOutRefusal } from "../../../src/sell-out.mjs";
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
  TaskPage,
  TileGrid,
  type Column,
} from "../../../src/ui";

type Stage = "start" | "checking" | "check" | "importing" | "done" | "failed";

const steps = ["Choose the file", "Check what will load", "Done"];

const header = {
  eyebrow: "Sell-out",
  title: "Import a sell-out file",
  description:
    "Send the Makro Dispo report, the Shoprite group portal report, or a wholesaler list. A later file of the same months replaces the earlier one. Nothing loads until you confirm.",
};

const back = { href: "/sell-out", label: "Sell-out" };

const issueColumns: Column<SellOutIssue>[] = [
  { key: "row", header: "Row", render: (issue) => issue.rowNumber, align: "end" },
  { key: "reason", header: "Problem", render: (issue) => issue.reason.replaceAll("-", " ") },
  { key: "key", header: "Store and product", render: (issue) => issue.rawKey ?? "—", mono: true },
];

export function SellOutImportFlow() {
  const [stage, setStage] = useState<Stage>("start");
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<SellOutImportResult | null>(null);
  const [result, setResult] = useState<SellOutImportResult | null>(null);
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
    const response = await previewSellOutFileAction(body);
    if (!response.ok) {
      setFailure(response.message);
      setStage("failed");
      return;
    }
    setPreview(response.result);
    setStage("check");
  }

  async function commit() {
    if (!file) {
      return;
    }
    setStage("importing");
    const body = new FormData();
    body.append("file", file);
    const response = await importSellOutFileAction(body);
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
        <FileDrop
          title="Drop the file here"
          hint="The Makro Dispo report is a .xls file. The Shoprite group portal report (Vendor Article Sales and Stock) and the wholesaler lists are .xlsx files. Send one wholesaler list in each file."
          accept=".xls,.xlsx,.txt,application/vnd.ms-excel,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,text/plain"
          busy={stage === "checking"}
          onFile={(chosen) => void check(chosen)}
        />
      ) : null}

      {stage === "failed" ? (
        <Stack gap="m">
          <Notice tone="error" title="The file was not loaded" live>
            <p>{failure}</p>
            <p>No sales data was changed.</p>
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
  preview: SellOutImportResult;
  fileName: string;
  busy: boolean;
  onImport: () => void;
  onRestart: () => void;
}) {
  if (preview.status === "Refused") {
    return (
      <Stack gap="m">
        <Notice tone="error" title="This file cannot load">
          {describeSellOutRefusal(preview.reason)}
        </Notice>
        {preview.issues.length > 0 ? <IssuesPanel issues={preview.issues} /> : null}
        <FormActions>
          <Button variant="primary" onClick={onRestart}>
            Choose a different file
          </Button>
        </FormActions>
      </Stack>
    );
  }

  const blocked = preview.reason === "too-many-bad-rows";

  return (
    <Stack gap="m">
      <Panel title="File" description="Nothing is loaded yet.">
        <KeyValueList
          items={[
            { label: "File", value: fileName },
            { label: "Kind", value: (preview.source && sellOutSources[preview.source]) ?? preview.source ?? "—" },
            { label: "Months", value: describeMonths(preview.months) },
          ]}
        />
      </Panel>

      <TileGrid min={170}>
        <StatTile label="Rows in the file" value={String(preview.rowsRead)} />
        <StatTile label="Store months to load" value={String(preview.rowsLoaded)} detail="One row for each store, product and month" />
        <StatTile label="New stores" value={String(preview.newStores)} />
        <StatTile
          label="Products to map"
          value={String(preview.unmappedProducts.length)}
          detail={preview.unmappedProducts.length > 0 ? "They load now. Map them after the import." : "All map to Acumatica items"}
          tone={preview.unmappedProducts.length > 0 ? "warning" : "default"}
        />
      </TileGrid>

      {blocked ? (
        <Notice tone="error" title="Too many rows cannot be read">
          {describeSellOutRefusal(preview.reason)}
        </Notice>
      ) : (
        <Notice tone="warning" title="A later file replaces the same months">
          The sales of {describeMonths(preview.months)} from earlier files of this kind are replaced by this file.
        </Notice>
      )}

      {preview.issues.length > 0 ? <IssuesPanel issues={preview.issues} /> : null}

      <FormActions>
        {busy ? (
          <Spinner label="Loading the file…" />
        ) : blocked ? (
          <Button variant="primary" onClick={onRestart}>
            Choose a different file
          </Button>
        ) : (
          <>
            <Button variant="primary" onClick={onImport}>
              Load {preview.rowsLoaded} store months
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

function IssuesPanel({ issues }: { issues: SellOutIssue[] }) {
  return (
    <Panel flush title={`${issues.length} ${issues.length === 1 ? "row" : "rows"} cannot be read`} description="These rows do not load. Fix them in the file, or ask the retailer for a new file.">
      <DataTable
        caption="Rows that cannot be read"
        columns={issueColumns}
        rows={issues.slice(0, 200)}
        rowKey={(issue) => `${issue.rowNumber}-${issue.reason}`}
        empty={<EmptyState title="No bad rows" />}
      />
    </Panel>
  );
}

function DoneStep({ result, onRestart }: { result: SellOutImportResult; onRestart: () => void }) {
  if (result.status !== "Loaded") {
    return (
      <Stack gap="m">
        <Notice tone="error" title="The file was not loaded" live>
          {describeSellOutRefusal(result.reason)}
        </Notice>
        <FormActions>
          <Button variant="primary" onClick={onRestart}>
            Choose a different file
          </Button>
        </FormActions>
      </Stack>
    );
  }

  return (
    <Stack gap="m">
      <Notice tone="success" title={`${result.rowsLoaded} store months loaded`} live>
        {describeMonths(result.months)} of the {(result.source && sellOutSources[result.source]) ?? "file"} are in.
      </Notice>
      {result.unmappedProducts.length > 0 ? (
        <Notice tone="warning" title={`${result.unmappedProducts.length} ${result.unmappedProducts.length === 1 ? "product needs" : "products need"} a map`}>
          Choose the Acumatica item for each one once. Later files use the same map.
        </Notice>
      ) : null}
      <FormActions>
        {result.unmappedProducts.length > 0 ? (
          <ButtonLink href="/sell-out/mapping" variant="primary">
            Map products
          </ButtonLink>
        ) : (
          <ButtonLink href="/sell-out" variant="primary">
            Back to sell-out
          </ButtonLink>
        )}
        <Button variant="secondary" onClick={onRestart}>
          Import another file
        </Button>
      </FormActions>
    </Stack>
  );
}
