"use client";

import { useRouter } from "next/navigation";
import { PageError } from "../../../../src/ui";

/** What error.tsx shows. In a real route, Next.js passes reset. */
export function ListErrorExample() {
  const router = useRouter();
  return <PageError title="Shoprite invoices" what="the Shoprite invoices" reset={() => router.push("/ui-kit/templates/list")} />;
}
