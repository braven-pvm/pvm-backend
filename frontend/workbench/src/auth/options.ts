import type { AuthOptions } from "next-auth";
import AzureADProvider from "next-auth/providers/azure-ad";

export const authOptions: AuthOptions = {
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
      if (account?.access_token) {
        token.accessToken = account.access_token;
      }

      return token;
    },
    async session({ session, token }) {
      session.accessToken = typeof token.accessToken === "string"
        ? token.accessToken
        : undefined;
      return session;
    },
  },
};
