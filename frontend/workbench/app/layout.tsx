import type { Metadata } from "next";
import { getServerSession } from "next-auth";
import { authOptions } from "../src/auth/options";
import { isDevelopmentBypass } from "../src/auth/config";
import { getCurrentUser } from "../src/auth/session";
import { navigationFor } from "../src/navigation";
import { AppShell, type EnvironmentKind } from "../src/ui";
import "./tokens.css";
import "./base.css";

export const metadata: Metadata = {
  title: {
    default: "PVM Backoffice",
    template: "%s · PVM Backoffice",
  },
  description: "PVM back-office operations: Shoprite, banking, stock and automations",
};

export default async function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  const bypass = isDevelopmentBypass();
  const session = bypass ? null : await getServerSession(authOptions);
  const user = await getCurrentUser();
  const userName =
    user?.displayName ?? user?.email ?? session?.user?.email ?? (bypass ? "developer@pvm.co.za" : undefined);

  return (
    <html lang="en">
      <body>
        <AppShell
          groups={navigationFor(user?.roles ?? [])}
          environment={environmentKind(process.env.NEXT_PUBLIC_PVM_ENVIRONMENT_NAME)}
          userName={userName}
          canSignOut={!bypass && Boolean(session)}
        >
          {children}
        </AppShell>
      </body>
    </html>
  );
}

function environmentKind(name: string | undefined): EnvironmentKind {
  const value = (name ?? "").trim().toLowerCase();
  if (value === "prod" || value === "production") {
    return "production";
  }

  if (value === "qa" || value === "test" || value === "staging") {
    return "qa";
  }

  return "development";
}
