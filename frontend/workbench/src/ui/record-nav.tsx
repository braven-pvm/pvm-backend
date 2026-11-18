"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, type ReactNode } from "react";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { keyboardStep } from "../document-list";
import { buttonClass } from "./button";
import styles from "./record-nav.module.css";

type RecordNavProps = {
  /** The record before this one in the list; leave it out on the first. */
  previousHref?: string;
  /** The record after this one in the list; leave it out on the last. */
  nextHref?: string;
  /** "3 of 12". */
  position: string;
  /** Names the records for screen readers: "document". */
  noun: string;
};

/**
 * Previous, the position and Next for a record opened from a list. On the
 * first or the last record the button is there but disabled. J or → opens the
 * next record and K or ← the previous one, unless a field or a dialog has the keys.
 * Spec: docs/design/design-system.md#recordnav
 */
export function RecordNav({ previousHref, nextHref, position, noun }: RecordNavProps) {
  const router = useRouter();

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const target = event.target instanceof HTMLElement ? event.target : null;
      const step = keyboardStep({
        key: event.key,
        target: target?.tagName ?? "",
        editable: target?.isContentEditable ?? false,
        modifier: event.altKey || event.ctrlKey || event.metaKey || event.shiftKey,
        dialogOpen: document.querySelector("dialog[open]") !== null,
      });
      const href = step === "next" ? nextHref : step === "previous" ? previousHref : undefined;
      if (href) {
        event.preventDefault();
        router.push(href);
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [previousHref, nextHref, router]);

  return (
    <nav className={styles.nav} aria-label={`Move between ${noun}s`}>
      <Step href={previousHref} label="Previous" hint={`Previous ${noun}`} keys="K">
        <ChevronLeft aria-hidden="true" />
      </Step>
      <span className={styles.position} aria-live="polite">
        {position}
      </span>
      <Step href={nextHref} label="Next" hint={`Next ${noun}`} keys="J" after>
        <ChevronRight aria-hidden="true" />
      </Step>
    </nav>
  );
}

function Step({
  href,
  label,
  hint,
  keys,
  after = false,
  children,
}: {
  href?: string;
  label: string;
  hint: string;
  keys: string;
  after?: boolean;
  children: ReactNode;
}) {
  const content = (
    <>
      {after ? null : children}
      <span className={styles.label}>{label}</span>
      {after ? children : null}
    </>
  );
  if (!href) {
    return (
      <span className={buttonClass("secondary")} role="link" aria-disabled="true" aria-label={hint} title={`No ${hint.toLowerCase()}`}>
        {content}
      </span>
    );
  }
  return (
    <Link className={buttonClass("secondary")} href={href} aria-label={hint} aria-keyshortcuts={keys} title={`${hint} (${keys})`}>
      {content}
    </Link>
  );
}
