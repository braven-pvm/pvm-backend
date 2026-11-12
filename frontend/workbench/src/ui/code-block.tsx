import styles from "./code-block.module.css";

type CodeBlockProps = {
  /** Read by screen readers: "Generated invoice XML". */
  label: string;
  /** The text to show. JSON is formatted with two-space indents. */
  code: string | null | undefined;
  language?: "xml" | "json" | "text";
  /** Shown when there is no code: "No XML yet. It is generated when the invoice is ready." */
  empty: string;
};

/**
 * A read-only payload: generated XML, a raw API response. Never editable.
 * It scrolls inside itself, up to 480px high.
 * Spec: docs/design/design-system.md#codeblock
 */
export function CodeBlock({ label, code, language = "text", empty }: CodeBlockProps) {
  if (!code) {
    return <p className={styles.empty}>{empty}</p>;
  }

  return (
    <pre className={styles.block} aria-label={label} tabIndex={0}>
      <code>{language === "json" ? formatJson(code) : code}</code>
    </pre>
  );
}

function formatJson(value: string) {
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}
