"use client";

import { signIn } from "next-auth/react";
import { Button } from "../../src/ui";

type SignInButtonProps = {
  callbackUrl: string;
};

export function SignInButton({ callbackUrl }: SignInButtonProps) {
  return (
    <Button variant="primary" onClick={() => signIn("azure-ad", { callbackUrl })}>
      Sign in with Microsoft
    </Button>
  );
}
