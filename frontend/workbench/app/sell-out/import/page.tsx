import { redirect } from "next/navigation";
import { hasAnyRole, requireWorkbenchUser } from "../../../src/auth/session";
import { SellOutImportFlow } from "./sell-out-import-flow";

export const dynamic = "force-dynamic";
export const metadata = { title: "Import a sell-out file" };

export default async function SellOutImportPage() {
  const user = await requireWorkbenchUser("/sell-out/import");
  if (!hasAnyRole(user, ["Admin", "Operator"])) {
    redirect("/sell-out");
  }

  return <SellOutImportFlow />;
}
