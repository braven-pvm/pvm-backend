import type { ReactNode } from "react";
import styles from "./stack.module.css";

export type Space = "2xs" | "xs" | "s" | "m" | "l" | "xl" | "xxl";

type StackProps = {
  /**
   * The space between children, from the 8pt scale. Related items get less
   * space: xs inside a control group, m between siblings, l between groups,
   * xl between page sections.
   */
  gap?: Space;
  direction?: "column" | "row";
  /** Rows only: let children wrap to the next line. */
  wrap?: boolean;
  align?: "start" | "center" | "end" | "stretch" | "baseline";
  justify?: "start" | "between" | "end";
  as?: "div" | "section" | "ul" | "ol";
  children: ReactNode;
};

/**
 * Lays out siblings with a gap from the spacing scale. Use it instead of
 * margins or inline styles. Spec: docs/design/design-system.md#stack
 */
export function Stack({
  gap = "m",
  direction = "column",
  wrap = false,
  align,
  justify,
  as: Tag = "div",
  children,
}: StackProps) {
  return (
    <Tag
      className={styles.stack}
      data-direction={direction}
      data-wrap={wrap || undefined}
      data-align={align}
      data-justify={justify}
      style={{ gap: `var(--space-${gap})` }}
    >
      {children}
    </Tag>
  );
}
