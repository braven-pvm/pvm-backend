import type { ReactNode } from "react";
import styles from "./page-header.module.css";

type PageHeaderProps = {
  /** The module or area the page belongs to, such as "Banking". */
  eyebrow?: string;
  title: string;
  description?: ReactNode;
  /** The page's actions. At most one primary button. */
  actions?: ReactNode;
};

export function PageHeader({ eyebrow, title, description, actions }: PageHeaderProps) {
  return (
    <header className={styles.header}>
      <div className={styles.text}>
        {eyebrow ? <p className={styles.eyebrow}>{eyebrow}</p> : null}
        <h1 className={styles.title}>{title}</h1>
        {description ? <p className={styles.description}>{description}</p> : null}
      </div>
      {actions ? <div className={styles.actions}>{actions}</div> : null}
    </header>
  );
}
