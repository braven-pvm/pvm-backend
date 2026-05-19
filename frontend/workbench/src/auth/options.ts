import type { AuthOptions } from "next-auth";
import AzureADProvider from "next-auth/providers/azure-ad";

export const authOptions: AuthOptions = {
  pages: {
    signIn: "/sign-in",
  },
  debug: process.env.AUTH_DEBUG === "true",
  logger: {
    error(code, metadata) {
      console.error("nextauth.error", code, metadata);
    },
    warn(code) {
      console.warn("nextauth.warn", code);
    },
    debug(code, metadata) {
      if (process.env.AUTH_DEBUG === "true") {
        console.info("nextauth.debug", code, metadata);
      }
    },
  },
  providers: [
    AzureADProvider({
      clientId: process.env.AUTH_ENTRA_CLIENT_ID ?? "",
      clientSecret: process.env.AUTH_ENTRA_CLIENT_SECRET ?? "",
      tenantId: process.env.AUTH_ENTRA_TENANT_ID,
      authorization: {
        params: {
          scope: `openid profile email offline_access ${process.env.AUTH_API_SCOPE ?? ""}`.trim(),
        },
      },
    }),
  ],
  callbacks: {
    async jwt({ token, account }) {
      if (account) {
        console.info("auth.jwt.account", {
          provider: account.provider,
          type: account.type,
          hasAccessToken: typeof account.access_token === "string",
          hasIdToken: typeof account.id_token === "string",
          expiresAt: account.expires_at,
        });
      }

      if (account?.access_token) {
        token.accessToken = account.access_token;
      }

      return token;
    },
    async session({ session, token }) {
      session.accessToken = typeof token.accessToken === "string"
        ? token.accessToken
        : undefined;
      console.info("auth.session", {
        email: session.user?.email,
        hasAccessToken: typeof session.accessToken === "string",
      });
      return session;
    },
  },
};
