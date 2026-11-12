import { AuthCard, Notice } from "../../src/ui";
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

  return (
    <AuthCard
      title="Sign in"
      description="Use your Microsoft work account."
      notice={
        params.error ? (
          <Notice tone="error" title="Sign-in failed">
            Try again. If it fails again, ask an Admin to check that your account is added.
          </Notice>
        ) : null
      }
      action={<SignInButton callbackUrl={callbackUrl} />}
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
