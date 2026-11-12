import type { ReactNode } from "react";
import styles from "./panel.module.css";

type PanelProps = {
  title?: string;
  description?: ReactNode;
  actions?: ReactNode;
  /** Remove the body padding, for tables that run edge to edge. */
  flush?: boolean;
  children: ReactNode;
};

export function Panel({ title, description, actions, flush = false, children }: PanelProps) {
  return (
    <section className={styles.panel} aria-label={title}>
      {title || actions ? (
        <div className={styles.head}>
          <div>
            {title ? <h2 className={styles.title}>{title}</h2> : null}
            {description ? <p className={styles.description}>{description}</p> : null}
          </div>
          {actions ? <div className={styles.actions}>{actions}</div> : null}
        </div>
      ) : null}
      <div className={flush ? styles.flush : styles.body}>{children}</div>
    </section>
  );
}
