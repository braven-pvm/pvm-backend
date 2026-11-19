import { FileText } from "lucide-react";
import Image from "next/image";
import styles from "./thumbnail.module.css";

type ThumbnailProps = {
  /**
   * A workbench URL that serves the JPEG, such as `/documents/{id}/thumbnail`.
   * Never a backend URL. Leave it out when there is no picture: a document icon shows.
   */
  src?: string | null;
  /** Read by screen readers: "First page of Fleet licence". */
  alt: string;
};

/**
 * A 48 × 64 preview of a scanned page in a list row, or a document icon when
 * there is none. Spec: docs/design/design-system.md#thumbnail
 */
export function Thumbnail({ src, alt }: ThumbnailProps) {
  return (
    <span className={styles.frame}>
      {src ? (
        // The picture comes from the workbench's own authenticated route: no image optimiser.
        <Image className={styles.image} src={src} alt={alt} width={48} height={64} unoptimized />
      ) : (
        <FileText size={24} aria-label={alt} role="img" />
      )}
    </span>
  );
}
