import { getBankingStatus, type BankAccountStatus } from "../../src/api/client";
import { hasAnyRole, requireWorkbenchUser } from "../../src/auth/session";
import { BankImportFlow } from "./bank-import-flow";

export const dynamic = "force-dynamic";

export default async function BankingPage() {
  const user = await requireWorkbenchUser("/banking");
  const canImport = hasAnyRole(user, ["Admin", "Operator"]);

  // The page still renders when Acumatica cannot be read, so the person sees why rather
  // than an empty screen. The import path reports the real error if it is tried.
  let accounts: BankAccountStatus[] = [];
  let statusFailed = false;
  try {
    accounts = (await getBankingStatus()).accounts;
  } catch {
    statusFailed = true;
  }

  return <BankImportFlow accounts={accounts} canImport={canImport} statusFailed={statusFailed} />;
}
