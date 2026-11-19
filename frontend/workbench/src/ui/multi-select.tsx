"use client";

import { ChevronDown, Search } from "lucide-react";
import { useEffect, useId, useRef, useState } from "react";
import { Button } from "./button";
import { Spinner } from "./spinner";
import styles from "./multi-select.module.css";

export type MultiSelectOption = {
  value: string;
  label: string;
  /** Already formatted, such as the total of the value: "R 12 400". */
  detail?: string;
};

type MultiSelectProps = {
  /** The plural name of the values: "Customers". */
  label: string;
  /** The values in use, with their labels. */
  selected: MultiSelectOption[];
  /**
   * Gives the values for a search text, the most useful first. A server action
   * works. It runs when the list opens and when the search text changes.
   */
  load: (search: string) => Promise<MultiSelectOption[]>;
  /** Receives the new values when the person presses "Apply". */
  onApply: (values: string[]) => void;
};

/**
 * A filter of many values with a search, such as some customers or some reps.
 * The button shows the number of values in use. The list opens under it, the
 * values in use first. Nothing changes until the person presses "Apply". Esc
 * or a click outside closes the list without a change.
 * Spec: docs/design/design-system.md#multiselect
 */
export function MultiSelect({ label, selected, load, onApply }: MultiSelectProps) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const [options, setOptions] = useState<MultiSelectOption[]>([]);
  const [loading, setLoading] = useState(false);
  const [failed, setFailed] = useState(false);
  const [chosen, setChosen] = useState<Map<string, MultiSelectOption>>(new Map());
  const rootRef = useRef<HTMLDivElement>(null);
  const searchRef = useRef<HTMLInputElement>(null);
  const listId = useId();

  // Load the values for the search text, 250 ms after the last key.
  useEffect(() => {
    if (!open) {
      return;
    }
    let current = true;
    const timer = window.setTimeout(() => {
      setLoading(true);
      setFailed(false);
      load(search)
        .then((result) => current && setOptions(result))
        .catch(() => current && setFailed(true))
        .finally(() => current && setLoading(false));
    }, 250);
    return () => {
      current = false;
      window.clearTimeout(timer);
    };
  }, [open, search, load]);

  // A click outside closes the list without a change.
  useEffect(() => {
    if (!open) {
      return;
    }
    const close = (event: MouseEvent) => {
      if (rootRef.current && !rootRef.current.contains(event.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener("mousedown", close);
    return () => document.removeEventListener("mousedown", close);
  }, [open]);

  const show = () => {
    setChosen(new Map(selected.map((option) => [option.value, option])));
    setSearch("");
    setOpen(true);
    window.setTimeout(() => searchRef.current?.focus(), 0);
  };

  const toggle = (option: MultiSelectOption) => {
    const next = new Map(chosen);
    if (next.has(option.value)) {
      next.delete(option.value);
    } else {
      next.set(option.value, option);
    }
    setChosen(next);
  };

  const apply = (values: string[]) => {
    setOpen(false);
    onApply(values);
  };

  // The values in use first, then the other values of the search.
  const rest = options.filter((option) => !chosen.has(option.value));
  const list = [...chosen.values(), ...rest];
  const summary = selected.length === 0 ? "All" : selected.length === 1 ? selected[0].label : `${selected.length} selected`;

  return (
    <div
      className={styles.root}
      ref={rootRef}
      onKeyDown={(event) => {
        if (event.key === "Escape" && open) {
          event.stopPropagation();
          setOpen(false);
        }
      }}
    >
      <button
        type="button"
        className={styles.trigger}
        data-active={selected.length > 0 || undefined}
        aria-expanded={open}
        aria-controls={open ? listId : undefined}
        onClick={() => (open ? setOpen(false) : show())}
      >
        <span className={styles.name}>{label}</span>
        <span className={styles.summary}>{summary}</span>
        <ChevronDown size={16} aria-hidden="true" />
      </button>
      {open ? (
        <div className={styles.panel} id={listId} role="group" aria-label={`Select ${label.toLowerCase()}`}>
          <label className={styles.search}>
            <Search size={16} aria-hidden="true" />
            <span className="sr-only">Search {label.toLowerCase()}</span>
            <input ref={searchRef} type="search" value={search} placeholder={`Search ${label.toLowerCase()}`} onChange={(event) => setSearch(event.target.value)} />
          </label>
          <div className={styles.list} aria-busy={loading}>
            {list.map((option) => (
              <label key={option.value} className={styles.option}>
                <input type="checkbox" checked={chosen.has(option.value)} onChange={() => toggle(option)} />
                <span className={styles.optionLabel}>{option.label}</span>
                {option.detail ? <span className={styles.detail}>{option.detail}</span> : null}
              </label>
            ))}
            {loading && list.length === 0 ? (
              <p className={styles.message}>
                <Spinner size="s" label="Loading" />
              </p>
            ) : null}
            {!loading && failed ? <p className={styles.message}>The values could not be read. Try again.</p> : null}
            {!loading && !failed && list.length === 0 ? <p className={styles.message}>No {label.toLowerCase()} match the search.</p> : null}
          </div>
          <div className={styles.actions}>
            <Button variant="tertiary" onClick={() => apply([])} disabled={selected.length === 0 && chosen.size === 0}>
              Clear
            </Button>
            <Button variant="primary" onClick={() => apply([...chosen.keys()])}>
              Apply{chosen.size > 0 ? ` (${chosen.size})` : ""}
            </Button>
          </div>
        </div>
      ) : null}
    </div>
  );
}
