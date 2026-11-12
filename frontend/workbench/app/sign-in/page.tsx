import { getServerSession } from "next-auth";
import { isDevelopmentBypass } from "../../src/auth/config";
import { authOptions } from "../../src/auth/options";
import { AuthCard, ButtonLink, Notice, Stack } from "../../src/ui";
import { SignInButton } from "./sign-in-button";

export const metadata = { title: "Sign in" };

type SignInPageProps = {
  searchParams: Promise<{
    callbackUrl?: string;
    error?: string;
  }>;
};

export default async function SignInPage({ searchParams }: SignInPageProps) {
  const params = await searchParams;
  const callbackUrl = normalizeCallbackUrl(params.callbackUrl);
  // A browser session with a token the API no longer accepts lands here. Say so,
  // so the person knows why they must sign in again, and can switch account.
  const session = isDevelopmentBypass() ? null : await getServerSession(authOptions);
  const expiredFor = session?.user?.email ?? session?.user?.name ?? null;

  return (
    <AuthCard
      title={expiredFor ? "Sign in again" : "Sign in"}
      description="Use your Microsoft work account."
      notice={
        params.error ? (
          <Notice tone="error" title="Sign-in failed">
            Try again. If it fails again, ask an Admin to check that your account is added.
          </Notice>
        ) : expiredFor ? (
          <Notice tone="info" title="Your session has ended">
            You were signed in as {expiredFor}. Sign in again to continue where you were.
          </Notice>
        ) : null
      }
      action={
        <Stack direction="row" wrap gap="s">
          <SignInButton callbackUrl={callbackUrl} />
          {expiredFor ? (
            <ButtonLink href="/api/auth/signout" variant="tertiary">
              Use another account
            </ButtonLink>
          ) : null}
        </Stack>
      }
    />
  );
}

function normalizeCallbackUrl(callbackUrl: string | undefined) {
  if (!callbackUrl || !callbackUrl.startsWith("/") || callbackUrl.startsWith("//")) {
    return "/";
  }

  if (callbackUrl.startsWith("/api/auth")) {
    return "/";
  }

  return callbackUrl;
}
