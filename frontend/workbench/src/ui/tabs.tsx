"use client";

import Link from "next/link";
import { useId, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import styles from "./tabs.module.css";

/*
 * Two kinds of tabs. Spec: docs/design/design-system.md#tabs
 * - TabNav: each tab is its own URL (a sub-page). Use it for module sections
 *   and record pages, so a tab can be shared and reloaded.
 * - Tabs: panels inside one page that need no URL, such as two views of the
 *   same data. Keyboard: arrow keys move between tabs, Home and End jump.
 */

export type TabLink = { label: string; href: string; current: boolean; count?: number };

export function TabNav({ label, tabs }: { label: string; tabs: TabLink[] }) {
  return (
    <nav className={styles.bar} aria-label={label}>
      {tabs.map((tab) => (
        <Link
          key={tab.href}
          className={styles.tab}
          href={tab.href}
          aria-current={tab.current ? "page" : undefined}
        >
          {tab.label}
          {tab.count !== undefined ? <span className={styles.count}>{tab.count}</span> : null}
        </Link>
      ))}
    </nav>
  );
}

export type TabPanel = { id: string; label: string; content: ReactNode };

export function Tabs({ label, tabs, defaultTab }: { label: string; tabs: TabPanel[]; defaultTab?: string }) {
  const baseId = useId();
  const [selected, setSelected] = useState(defaultTab ?? tabs[0]?.id);
  const refs = useRef<Array<HTMLButtonElement | null>>([]);

  function onKeyDown(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    const last = tabs.length - 1;
    const target =
      event.key === "ArrowRight"
        ? (index + 1) % tabs.length
        : event.key === "ArrowLeft"
          ? (index - 1 + tabs.length) % tabs.length
          : event.key === "Home"
            ? 0
            : event.key === "End"
              ? last
              : null;
    if (target === null) {
      return;
    }
    event.preventDefault();
    setSelected(tabs[target].id);
    refs.current[target]?.focus();
  }

  return (
    <div className={styles.tabs}>
      <div className={styles.bar} role="tablist" aria-label={label}>
        {tabs.map((tab, index) => {
          const isSelected = tab.id === selected;
          return (
            <button
              key={tab.id}
              ref={(element) => {
                refs.current[index] = element;
              }}
              type="button"
              role="tab"
              id={`${baseId}-tab-${tab.id}`}
              aria-controls={`${baseId}-panel-${tab.id}`}
              aria-selected={isSelected}
              tabIndex={isSelected ? 0 : -1}
              className={styles.tab}
              onClick={() => setSelected(tab.id)}
              onKeyDown={(event) => onKeyDown(event, index)}
            >
              {tab.label}
            </button>
          );
        })}
      </div>
      {tabs.map((tab) => (
        <div
          key={tab.id}
          role="tabpanel"
          id={`${baseId}-panel-${tab.id}`}
          aria-labelledby={`${baseId}-tab-${tab.id}`}
          hidden={tab.id !== selected}
          tabIndex={0}
          className={styles.panel}
        >
          {tab.content}
        </div>
      ))}
    </div>
  );
}
