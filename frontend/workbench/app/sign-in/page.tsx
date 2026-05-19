import { SignInButton } from "./sign-in-button";

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
    <main className="auth-shell">
      <section className="auth-panel">
        <h1>Sign in</h1>
        <p>Use your Microsoft account to access the PVM workbench.</p>
        {params.error ? (
          <p className="auth-error">Sign-in failed. Try again or contact an admin.</p>
        ) : null}
        <SignInButton callbackUrl={callbackUrl} />
      </section>
    </main>
  );
}

function normalizeCallbackUrl(callbackUrl: string | undefined) {
  if (!callbackUrl || !callbackUrl.startsWith("/") || callbackUrl.startsWith("//")) {
    return "/invoices";
  }

  if (callbackUrl.startsWith("/api/auth")) {
    return "/invoices";
  }

  return callbackUrl;
}
