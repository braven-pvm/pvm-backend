import { Upload } from "lucide-react";
import { getBankingStatus, type BankAccountStatus, type BankStatementSummary } from "../../src/api/client";
import { hasAnyRole, requireWorkbenchUser } from "../../src/auth/session";
import { formatMoneyZar, formatStatementDate } from "../../src/formatters.mjs";
import { ButtonLink, DashboardPage, DataTable, EmptyState, StatTile, TileGrid, type Column } from "../../src/ui";

export const dynamic = "force-dynamic";
export const metadata = { title: "Banking" };

type StatementRow = BankStatementSummary & { bank: string };

const statementColumns: Column<StatementRow>[] = [
  { key: "reference", header: "Statement", render: (row) => row.referenceNbr, mono: true },
  { key: "bank", header: "Bank", render: (row) => row.bank },
  {
    key: "period",
    header: "Covers",
    render: (row) =>
      row.startBalanceDate === row.endBalanceDate
        ? formatStatementDate(row.endBalanceDate)
        : `${formatStatementDate(row.startBalanceDate)} to ${formatStatementDate(row.endBalanceDate)}`,
  },
  { key: "lines", header: "Transactions", render: (row) => row.lineCount, align: "end" },
  { key: "closing", header: "Closing balance", render: (row) => formatMoneyZar(row.endingBalance), align: "end" },
];

function accountDetail(account: BankAccountStatus) {
  const arrives = account.mode === "Automatic" || account.mode === "Feed" ? "Arrives automatically" : "You upload it";
  if (!account.available) {
    return `${arrives} · could not read Acumatica`;
  }
  return account.importedThrough ? `${arrives} · imported through ${formatStatementDate(account.importedThrough)}` : `${arrives} · nothing imported yet`;
}

/** The banking overview: each account, and the statements recently created in Acumatica. */
export default async function BankingPage() {
  const user = await requireWorkbenchUser("/banking");
  const canImport = hasAnyRole(user, ["Admin", "Operator"]);

  // The page still renders when Acumatica cannot be read, so the person sees why.
  let accounts: BankAccountStatus[] = [];
  let statusFailed = false;
  try {
    accounts = (await getBankingStatus()).accounts;
  } catch {
    statusFailed = true;
  }

  const statements: StatementRow[] = accounts
    .flatMap((account) => account.recentStatements.map((statement) => ({ ...statement, bank: account.bank })))
    .sort((a, b) => b.endBalanceDate.localeCompare(a.endBalanceDate));

  return (
    <DashboardPage
      header={{
        eyebrow: "Operations",
        title: "Banking",
        description: "Bank statements into Acumatica, ready for you to reconcile. Investec arrives by itself. Nedbank you upload.",
        actions: canImport ? (
          <ButtonLink href="/banking/import" variant="primary">
            <Upload aria-hidden="true" />
            Import a Nedbank statement
          </ButtonLink>
        ) : null,
      }}
      attention={{
        allClear: "Every account was read from Acumatica.",
        items: statusFailed
          ? [
              {
                id: "acumatica",
                status: "Failed",
                title: "Acumatica could not be reached",
                detail: "The dates below may be out of date. Try again in a few minutes before you import.",
              },
            ]
          : [],
      }}
      numbers={{
        title: "Accounts",
        content: (
          <TileGrid min={260}>
            {accounts.map((account) => (
              <StatTile
                key={account.cashAccount}
                label={`${account.bank} · ${account.cashAccount}`}
                value={account.available && account.importedThrough ? formatMoneyZar(account.closingBalance) : "—"}
                detail={accountDetail(account)}
                tone={account.available ? "default" : "warning"}
              />
            ))}
          </TileGrid>
        ),
      }}
      activity={{
        title: "Recent statements",
        content: (
          <DataTable
            caption="Recent statements in Acumatica"
            columns={statementColumns}
            rows={statements}
            rowKey={(row) => `${row.bank}-${row.referenceNbr}`}
            empty={<EmptyState title="No statements imported yet" description="Statements show here after the first import." />}
          />
        ),
      }}
    />
  );
}
