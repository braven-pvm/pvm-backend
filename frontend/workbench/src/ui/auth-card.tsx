import type { ReactNode } from "react";
import styles from "./auth-card.module.css";

type AuthCardProps = {
  title: string;
  description: ReactNode;
  /** A Notice, such as a sign-in error. */
  notice?: ReactNode;
  /** One primary action. */
  action?: ReactNode;
};

/**
 * The only layout for pages without a signed-in user: sign-in and access
 * denied. There is no sidebar on these pages.
 * Spec: docs/design/design-system.md#signed-out-pages
 */
export function AuthCard({ title, description, notice, action }: AuthCardProps) {
  return (
    <main className={styles.page} data-ds>
      <section className={styles.card} aria-labelledby="auth-title">
        <p className={styles.brand}>
          <span className={styles.mark} aria-hidden="true">
            P
          </span>
          PVM Backoffice
        </p>
        <h1 className={styles.title} id="auth-title">
          {title}
        </h1>
        <div className={styles.description}>{description}</div>
        {notice}
        {action ? <div className={styles.action}>{action}</div> : null}
      </section>
    </main>
  );
}
