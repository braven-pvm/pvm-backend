import type {
  InputHTMLAttributes,
  ReactNode,
  SelectHTMLAttributes,
  TextareaHTMLAttributes,
} from "react";
import { CircleX } from "lucide-react";
import styles from "./field.module.css";

/*
 * Form fields. Every field has a visible label above it, an optional hint
 * between the label and the control, and an error below the label when the
 * value is wrong. Spec: docs/design/design-system.md#fields
 */

export type FieldWidth = "s" | "m" | "l" | "full";

type FrameProps = {
  /** The control's id. Defaults to its name. */
  id: string;
  label: string;
  hint?: ReactNode;
  /** What is wrong and how to fix it: "Enter a date after 13 Sep 2026". */
  error?: string;
  required?: boolean;
  /** false hides "(optional)", for filter fields where every field is optional. */
  showOptional?: boolean;
  width?: FieldWidth;
  children: ReactNode;
};

function describedBy(id: string, hint: ReactNode, error?: string) {
  const ids = [hint ? `${id}-hint` : null, error ? `${id}-error` : null].filter(Boolean);
  return ids.length > 0 ? ids.join(" ") : undefined;
}

function FieldFrame({ id, label, hint, error, required, showOptional = true, width = "m", children }: FrameProps) {
  return (
    <div className={styles.field} data-width={width} data-invalid={error ? true : undefined}>
      <label className={styles.label} htmlFor={id}>
        {label}
        {required ? (
          <span className={styles.required} aria-hidden="true">
            {" *"}
          </span>
        ) : showOptional ? (
          <span className={styles.optional}> (optional)</span>
        ) : null}
      </label>
      {hint ? (
        <p className={styles.hint} id={`${id}-hint`}>
          {hint}
        </p>
      ) : null}
      {error ? (
        <p className={styles.error} id={`${id}-error`}>
          <CircleX size={16} aria-hidden="true" />
          {error}
        </p>
      ) : null}
      {children}
    </div>
  );
}

type Common = {
  name: string;
  id?: string;
  label: string;
  hint?: ReactNode;
  error?: string;
  width?: FieldWidth;
  /** false hides "(optional)", for filter fields where every field is optional. */
  showOptional?: boolean;
};

export function TextField({
  name,
  id = name,
  label,
  hint,
  error,
  width,
  required,
  showOptional,
  ...props
}: Common & Omit<InputHTMLAttributes<HTMLInputElement>, "name" | "id">) {
  return (
    <FieldFrame
      id={id}
      label={label}
      hint={hint}
      error={error}
      required={required}
      showOptional={showOptional}
      width={width}
    >
      <input
        className={styles.control}
        id={id}
        name={name}
        required={required}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy(id, hint, error)}
        {...props}
      />
    </FieldFrame>
  );
}

export function TextareaField({
  name,
  id = name,
  label,
  hint,
  error,
  width = "l",
  required,
  showOptional,
  rows = 4,
  ...props
}: Common & Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, "name" | "id">) {
  return (
    <FieldFrame
      id={id}
      label={label}
      hint={hint}
      error={error}
      required={required}
      showOptional={showOptional}
      width={width}
    >
      <textarea
        className={`${styles.control} ${styles.textarea}`}
        id={id}
        name={name}
        rows={rows}
        required={required}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy(id, hint, error)}
        {...props}
      />
    </FieldFrame>
  );
}

export type Option = { value: string; label: string };

/** Use only for more than 10 options. For 10 or fewer, use RadioGroup. */
export function SelectField({
  name,
  id = name,
  label,
  hint,
  error,
  width,
  required,
  showOptional,
  options,
  placeholder,
  ...props
}: Common & { options: Option[]; placeholder?: string } & Omit<
    SelectHTMLAttributes<HTMLSelectElement>,
    "name" | "id"
  >) {
  return (
    <FieldFrame
      id={id}
      label={label}
      hint={hint}
      error={error}
      required={required}
      showOptional={showOptional}
      width={width}
    >
      <select
        className={`${styles.control} ${styles.select}`}
        id={id}
        name={name}
        required={required}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy(id, hint, error)}
        {...props}
      >
        {placeholder ? <option value="">{placeholder}</option> : null}
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </FieldFrame>
  );
}

/** A file input. The hint names the accepted format and where to get the file. */
export function FileField({
  name,
  id = name,
  label,
  hint,
  error,
  width = "l",
  required,
  showOptional,
  ...props
}: Common & Omit<InputHTMLAttributes<HTMLInputElement>, "name" | "id" | "type">) {
  return (
    <FieldFrame
      id={id}
      label={label}
      hint={hint}
      error={error}
      required={required}
      showOptional={showOptional}
      width={width}
    >
      <input
        className={`${styles.control} ${styles.file}`}
        type="file"
        id={id}
        name={name}
        required={required}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy(id, hint, error)}
        {...props}
      />
    </FieldFrame>
  );
}

/**
 * One on/off choice that takes effect when the form is submitted. Phrase the
 * label positively: "Include zero-value lines", not "Do not exclude…".
 */
export function CheckboxField({
  name,
  id = name,
  label,
  hint,
  ...props
}: Omit<Common, "error" | "width"> & Omit<InputHTMLAttributes<HTMLInputElement>, "name" | "id" | "type">) {
  return (
    <div className={styles.check}>
      <input
        className={styles.checkInput}
        type="checkbox"
        id={id}
        name={name}
        aria-describedby={hint ? `${id}-hint` : undefined}
        {...props}
      />
      <div className={styles.checkText}>
        <label className={styles.checkLabel} htmlFor={id}>
          {label}
        </label>
        {hint ? (
          <p className={styles.hint} id={`${id}-hint`}>
            {hint}
          </p>
        ) : null}
      </div>
    </div>
  );
}

type RadioOption = Option & { hint?: string };

/** One choice from 2 to 10 options, all visible. */
export function RadioGroup({
  name,
  label,
  hint,
  error,
  options,
  defaultValue,
  required,
  showOptional = true,
}: Omit<Common, "id" | "width"> & {
  options: RadioOption[];
  defaultValue?: string;
  required?: boolean;
}) {
  const groupId = `${name}-group`;
  return (
    <fieldset
      className={styles.fieldset}
      aria-describedby={describedBy(groupId, hint, error)}
      data-invalid={error ? true : undefined}
    >
      <legend className={styles.label}>
        {label}
        {required ? (
          <span className={styles.required} aria-hidden="true">
            {" *"}
          </span>
        ) : showOptional ? (
          <span className={styles.optional}> (optional)</span>
        ) : null}
      </legend>
      {hint ? (
        <p className={styles.hint} id={`${groupId}-hint`}>
          {hint}
        </p>
      ) : null}
      {error ? (
        <p className={styles.error} id={`${groupId}-error`}>
          <CircleX size={16} aria-hidden="true" />
          {error}
        </p>
      ) : null}
      <div className={styles.radios}>
        {options.map((option) => {
          const id = `${name}-${option.value}`;
          return (
            <div className={styles.check} key={option.value}>
              <input
                className={styles.checkInput}
                type="radio"
                id={id}
                name={name}
                value={option.value}
                defaultChecked={option.value === defaultValue}
                required={required}
                aria-describedby={option.hint ? `${id}-hint` : undefined}
              />
              <div className={styles.checkText}>
                <label className={styles.checkLabel} htmlFor={id}>
                  {option.label}
                </label>
                {option.hint ? (
                  <p className={styles.hint} id={`${id}-hint`}>
                    {option.hint}
                  </p>
                ) : null}
              </div>
            </div>
          );
        })}
      </div>
    </fieldset>
  );
}

/** Several checkboxes under one label, for a choice of more than one option. */
export function CheckboxGroup({ label, hint, children }: { label: string; hint?: ReactNode; children: ReactNode }) {
  return (
    <fieldset className={styles.fieldset}>
      <legend className={styles.label}>{label}</legend>
      {hint ? <p className={styles.hint}>{hint}</p> : null}
      <div className={styles.radios}>{children}</div>
    </fieldset>
  );
}

/** Groups the fields of one form with the form spacing. Put actions last. */
export function FormLayout({ children }: { children: ReactNode }) {
  return <div className={styles.form}>{children}</div>;
}

/** The row of form actions: the primary submit first, then a tertiary cancel. */
export function FormActions({ children }: { children: ReactNode }) {
  return <div className={styles.actions}>{children}</div>;
}
