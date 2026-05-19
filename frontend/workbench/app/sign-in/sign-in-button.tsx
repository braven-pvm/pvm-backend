"use client";

import { signIn } from "next-auth/react";

type SignInButtonProps = {
  callbackUrl: string;
};

export function SignInButton({ callbackUrl }: SignInButtonProps) {
  return (
    <button
      className="button"
      type="button"
      onClick={() => signIn("azure-ad", { callbackUrl })}
    >
      Sign in with Microsoft
    </button>
  );
}
