"use client";

import { useState } from "react";
import { saveStatementSettingsAction } from "../../actions";
import { FileDrop, Notice, Stack } from "../../../src/ui";

/** Uploads one settings file. Nothing is saved when a name does not resolve; the page lists those names. */
export function SettingsUpload() {
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<{ saved: boolean; problems: string[] } | null>(null);

  async function upload(file: File) {
    setBusy(true);
    setResult(null);
    const body = new FormData();
    body.append("file", file);
    setResult(await saveStatementSettingsAction(body));
    setBusy(false);
  }

  return (
    <Stack gap="m">
      <FileDrop title="Drop the settings file here" hint="A .json file, as the download gives it." accept=".json,application/json" busy={busy} onFile={(file) => void upload(file)} />
      {result?.saved ? (
        <Notice tone="success" title="The settings are saved" live>
          The statements use them from the month in validFrom.
        </Notice>
      ) : null}
      {result && !result.saved ? (
        <Notice tone="error" title="Nothing was saved" live>
          <ul>
            {result.problems.map((problem) => (
              <li key={problem}>{problem}</li>
            ))}
          </ul>
        </Notice>
      ) : null}
    </Stack>
  );
}
