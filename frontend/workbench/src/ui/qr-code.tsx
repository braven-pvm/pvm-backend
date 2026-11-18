import styles from "./qr-code.module.css";

type QrCodeProps = {
  /** Read by screen readers: "Setup code for Dispatch phone 1". */
  label: string;
  /**
   * The QR code as SVG markup, made on the server by the qrcode package
   * (QRCode.toString with type "svg"). Never markup from user input.
   */
  svg: string;
};

/**
 * A QR code for a phone to scan from the screen. It is 320px wide, the code itself at least
 * 280px (no frame padding on a phone), black on white in both themes.
 * Spec: docs/design/design-system.md#qrcode
 */
export function QrCode({ label, svg }: QrCodeProps) {
  return <div className={styles.frame} role="img" aria-label={label} dangerouslySetInnerHTML={{ __html: svg }} />;
}
