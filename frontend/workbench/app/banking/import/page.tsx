import { redirect } from "next/navigation";
import { getBankingStatus, type BankAccountStatus } from "../../../src/api/client";
import { hasAnyRole, requireWorkbenchUser } from "../../../src/auth/session";
import { BankImportFlow } from "./bank-import-flow";

export const dynamic = "force-dynamic";
export const metadata = { title: "Import a Nedbank statement" };

export default async function BankImportPage() {
  const user = await requireWorkbenchUser("/banking/import");
  if (!hasAnyRole(user, ["Admin", "Operator"])) {
    redirect("/banking");
  }

  // The flow still works when the status cannot be read; the import reports the real error.
  let accounts: BankAccountStatus[] = [];
  let statusFailed = false;
  try {
    accounts = (await getBankingStatus()).accounts;
  } catch {
    statusFailed = true;
  }

  return <BankImportFlow uploadAccount={accounts.find((account) => account.mode === "Upload")} statusFailed={statusFailed} />;
}
