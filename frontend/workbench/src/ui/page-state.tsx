"use client";

import { RotateCw } from "lucide-react";
import { Button } from "./button";
import { Notice } from "./notice";
import { PageFrame } from "./page-frame";
import { Spinner } from "./spinner";
import styles from "./page-state.module.css";

/**
 * The content of a route's loading.tsx: the page title and a spinner with a
 * label that names what is loading. Spec: docs/design/design-system.md#page-states
 */
export function PageLoading({ title, label }: { title: string; label: string }) {
  return (
    <PageFrame>
      <h1 className={styles.title}>{title}</h1>
      <Spinner label={label} />
    </PageFrame>
  );
}

/**
 * The content of a route's error.tsx. It says what failed and offers
 * "Try again", which calls the reset function that Next.js passes in.
 */
export function PageError({
  title,
  what,
  reset,
}: {
  title: string;
  /** What could not load: "the Shoprite invoices". */
  what: string;
  reset: () => void;
}) {
  return (
    <PageFrame>
      <h1 className={styles.title}>{title}</h1>
      <Notice
        tone="error"
        title={`Could not load ${what}`}
        action={
          <Button variant="secondary" onClick={reset}>
            <RotateCw aria-hidden="true" />
            Try again
          </Button>
        }
      >
        The backoffice API did not answer. Try again. If it fails again, check Settings, Connections.
      </Notice>
    </PageFrame>
  );
}
