"use client";

import { FileUp } from "lucide-react";
import { useId, useRef, useState } from "react";
import { Spinner } from "./spinner";
import styles from "./file-drop.module.css";

type FileDropProps = {
  /** "Drop your statement here". */
  title: string;
  /** Which file, and where to get it. */
  hint: string;
  accept: string;
  /** Called with the chosen file. The parent checks it at once, without a submit button. */
  onFile: (file: File) => void;
  /** True while the parent reads the file. */
  busy?: boolean;
  busyLabel?: string;
};

/**
 * The first step of a Task page that starts from a file. Drop a file on it,
 * or press "Choose a file". Spec: docs/design/design-system.md#filedrop
 */
export function FileDrop({ title, hint, accept, onFile, busy = false, busyLabel = "Reading the file…" }: FileDropProps) {
  const [dragging, setDragging] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const id = useId();

  return (
    <div
      className={styles.drop}
      data-dragging={dragging || undefined}
      onDragOver={(event) => {
        event.preventDefault();
        setDragging(true);
      }}
      onDragLeave={() => setDragging(false)}
      onDrop={(event) => {
        event.preventDefault();
        setDragging(false);
        const dropped = event.dataTransfer.files?.[0];
        if (dropped && !busy) {
          onFile(dropped);
        }
      }}
    >
      <FileUp className={styles.icon} size={28} aria-hidden="true" />
      <p className={styles.title}>{title}</p>
      <p className={styles.hint} id={`${id}-hint`}>
        {hint}
      </p>
      {busy ? (
        <Spinner label={busyLabel} />
      ) : (
        <label className={styles.button} htmlFor={id}>
          Choose a file
        </label>
      )}
      <input
        ref={inputRef}
        className="sr-only"
        id={id}
        type="file"
        accept={accept}
        aria-describedby={`${id}-hint`}
        disabled={busy}
        onChange={(event) => {
          const chosen = event.target.files?.[0];
          if (chosen) {
            onFile(chosen);
          }
          // Let the same file be chosen again after a failure.
          if (inputRef.current) {
            inputRef.current.value = "";
          }
        }}
      />
    </div>
  );
}
