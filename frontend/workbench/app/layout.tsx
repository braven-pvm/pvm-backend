import type { Metadata } from "next";
import Link from "next/link";
import { getServerSession } from "next-auth";
import { authOptions } from "../src/auth/options";
import { isDevelopmentBypass } from "../src/auth/config";
import "./globals.css";

export const metadata: Metadata = {
  title: "PVM Invoice Workbench",
  description: "Shoprite invoice submission workbench",
};

export default async function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  const session = isDevelopmentBypass()
    ? { user: { email: "developer@pvm.co.za" } }
    : await getServerSession(authOptions);

  return (
    <html lang="en">
      <body>
        <header className="app-header">
          <Link className="brand" href="/">
            PVM Workbench
          </Link>
          <nav aria-label="Primary navigation">
            <Link href="/invoices">Invoices</Link>
            <Link href="/admin/users">Users</Link>
          </nav>
          <div className="user-chip">
            {session?.user?.email ? (
              <>
                <span>{session.user.email}</span>
                {!isDevelopmentBypass() ? (
                  <Link href="/api/auth/signout">Sign out</Link>
                ) : null}
              </>
            ) : (
              <Link href="/sign-in?callbackUrl=/invoices">Sign in</Link>
            )}
          </div>
        </header>
        {children}
      </body>
    </html>
  );
}
