"use client";

import { ArrowLeft, ArrowRight, ChevronRight, X } from "lucide-react";
import { useId } from "react";
import styles from "./order-picker.module.css";

type OrderPickerProps = {
  /** What the order is for: "Drill-down". */
  label: string;
  /** All the items that can be in the order. */
  options: { value: string; label: string }[];
  /** The items in use, in order. */
  value: string[];
  /** The most items. With this number in use, "Add" does not show. */
  max?: number;
  onChange: (value: string[]) => void;
};

/**
 * An order of some items, such as the levels of a drill-down: Rep, then
 * Customer, then Product. Each item moves left or right, or goes out. A select
 * adds an item at the end. One item always stays.
 * Spec: docs/design/design-system.md#orderpicker
 */
export function OrderPicker({ label, options, value, max = options.length, onChange }: OrderPickerProps) {
  const labelId = useId();
  const name = (item: string) => options.find((option) => option.value === item)?.label ?? item;
  const unused = options.filter((option) => !value.includes(option.value));

  const move = (index: number, by: number) => {
    const next = [...value];
    [next[index], next[index + by]] = [next[index + by], next[index]];
    onChange(next);
  };

  return (
    <div className={styles.root} role="group" aria-labelledby={labelId}>
      <span className={styles.label} id={labelId}>
        {label}
      </span>
      <ol className={styles.list}>
        {value.map((item, index) => (
          <li key={item} className={styles.item}>
            {index > 0 ? <ChevronRight className={styles.separator} size={16} aria-hidden="true" /> : null}
            <span className={styles.chip}>
              <button type="button" className={styles.icon} disabled={index === 0} onClick={() => move(index, -1)} title={`Move ${name(item)} up one level`}>
                <ArrowLeft size={14} aria-hidden="true" />
                <span className="sr-only">Move {name(item)} up one level</span>
              </button>
              <span className={styles.name}>{name(item)}</span>
              <button
                type="button"
                className={styles.icon}
                disabled={index === value.length - 1}
                onClick={() => move(index, 1)}
                title={`Move ${name(item)} down one level`}
              >
                <ArrowRight size={14} aria-hidden="true" />
                <span className="sr-only">Move {name(item)} down one level</span>
              </button>
              <button
                type="button"
                className={styles.icon}
                disabled={value.length === 1}
                onClick={() => onChange(value.filter((other) => other !== item))}
                title={`Remove ${name(item)}`}
              >
                <X size={14} aria-hidden="true" />
                <span className="sr-only">Remove {name(item)}</span>
              </button>
            </span>
          </li>
        ))}
      </ol>
      {unused.length > 0 && value.length < max ? (
        <select
          className={styles.add}
          aria-label={`Add a level to the ${label.toLowerCase()}`}
          value=""
          onChange={(event) => {
            if (event.target.value) {
              onChange([...value, event.target.value]);
            }
          }}
        >
          <option value="">+ Add level</option>
          {unused.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      ) : null}
    </div>
  );
}
