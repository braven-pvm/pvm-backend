import { AuthCard, ButtonLink } from "../../src/ui";

export const metadata = { title: "No access" };

export default function AccessDeniedPage() {
  return (
    <AuthCard
      title="No access yet"
      description="Your Microsoft account signed in, but it is not added to PVM Backoffice. Ask an Admin to add you, then sign in again."
      action={
        <ButtonLink href="/api/auth/signout" variant="secondary">
          Sign out
        </ButtonLink>
      }
    />
  );
}
