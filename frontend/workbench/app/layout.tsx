import type { Metadata } from "next";
import Link from "next/link";
import "./globals.css";

export const metadata: Metadata = {
  title: "PVM Invoice Workbench",
  description: "Shoprite invoice submission workbench",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <body>
        <header className="app-header">
          <Link className="brand" href="/">
            PVM Workbench
          </Link>
          <nav aria-label="Primary navigation">
            <Link href="/invoices">Invoices</Link>
          </nav>
        </header>
        {children}
      </body>
    </html>
  );
}
