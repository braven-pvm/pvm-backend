"use client";

import { useState } from "react";
import { useFormStatus } from "react-dom";
import { importNedbankStatementAction } from "../actions";

function ImportButton({ hasFile }: { hasFile: boolean }) {
  const { pending } = useFormStatus();

  return (
    <button className="button" disabled={!hasFile || pending} type="submit">
      {pending ? "Importing" : "Import statement"}
    </button>
  );
}

export function NedbankUploadForm() {
  const [fileName, setFileName] = useState<string | null>(null);

  return (
    <form action={importNedbankStatementAction} className="inline-form">
      <label>
        <span>Nedbank OFX file *</span>
        <input
          accept=".ofx,application/x-ofx,text/plain"
          name="file"
          onChange={(event) => setFileName(event.target.files?.[0]?.name ?? null)}
          required
          type="file"
        />
      </label>
      {fileName ? <p className="field-note">Selected file: {fileName}</p> : null}
      <ImportButton hasFile={Boolean(fileName)} />
    </form>
  );
}
