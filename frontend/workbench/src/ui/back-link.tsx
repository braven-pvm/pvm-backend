import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import styles from "./back-link.module.css";

/**
 * The way up from a Record or Task page to its list. The label names the
 * list: "Shoprite invoices", never "Back". Spec: docs/design/design-system.md#backlink
 */
export function BackLink({ href, label }: { href: string; label: string }) {
  return (
    <Link className={styles.link} href={href}>
      <ArrowLeft size={16} aria-hidden="true" />
      {label}
    </Link>
  );
}
