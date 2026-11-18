import { ExternalLink } from "lucide-react";
import styles from "./pdf-frame.module.css";

type PdfFrameProps = {
  /** A workbench URL that serves the PDF, such as /documents/{id}/file. Never a backend URL. */
  src: string;
  /** Names the document for screen readers: "Proof of delivery uploaded 17 Nov, 09:12". */
  title: string;
};

/**
 * A PDF shown in the page with the browser's own viewer, and a link that opens
 * it in a new tab, for a phone or a browser that does not show PDFs inline.
 * Spec: docs/design/design-system.md#pdfframe
 */
export function PdfFrame({ src, title }: PdfFrameProps) {
  return (
    <div className={styles.frame}>
      <iframe className={styles.viewer} src={src} title={title} />
      <a className={styles.open} href={src} target="_blank" rel="noopener">
        <ExternalLink size={16} aria-hidden="true" />
        Open the PDF in a new tab
      </a>
    </div>
  );
}
