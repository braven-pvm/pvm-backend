import { ExternalLink } from "lucide-react";
import styles from "./pdf-frame.module.css";

type PdfFrameProps = {
  /** A workbench URL that serves the PDF, such as /documents/{id}/file. Never a backend URL. */
  src: string;
  /** Names the document for screen readers: "Proof of delivery uploaded 17 Nov, 09:12". */
  title: string;
  /**
   * default: 720px high (at most 80 % of the screen), 480px on a phone.
   * fill: as high as its container, opened at page width. For a page built
   * around the PDF, such as the document page (ViewerPage); 75 % of the screen on a phone.
   */
  size?: "default" | "fill";
};

/**
 * A PDF shown in the page with the browser's own viewer, and a link that opens
 * it in a new tab, for a phone or a browser that does not show PDFs inline.
 * Spec: docs/design/design-system.md#pdfframe
 */
export function PdfFrame({ src, title, size = "default" }: PdfFrameProps) {
  // Asks the browser's viewer to open at page width, without the page thumbnails beside it.
  const viewerSrc = size === "fill" ? `${src}#navpanes=0&view=FitH` : src;
  return (
    <div className={styles.frame} data-size={size}>
      <iframe className={styles.viewer} src={viewerSrc} title={title} />
      <a className={styles.open} href={src} target="_blank" rel="noopener">
        <ExternalLink size={16} aria-hidden="true" />
        Open the PDF in a new tab
      </a>
    </div>
  );
}
