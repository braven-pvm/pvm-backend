import type { ReactNode } from "react";
import styles from "./page-frame.module.css";

type PageFrameProps = {
  /**
   * wide (1280px): Dashboard, List and Record pages.
   * narrow (800px): Task and Settings pages, which are one column of steps or forms.
   */
  width?: "wide" | "narrow";
  children: ReactNode;
};

/**
 * The <main> element of a converted page. It sets the width, the padding and
 * data-ds, so old globals.css element rules do not apply inside it. The page
 * templates use it. Spec: docs/design/design-system.md#pageframe
 */
export function PageFrame({ width = "wide", children }: PageFrameProps) {
  return (
    <main className={styles.page} data-width={width} data-ds>
      {children}
    </main>
  );
}
