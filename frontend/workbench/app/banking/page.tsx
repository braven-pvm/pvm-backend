import { Upload } from "lucide-react";
import { getBankingStatus, type BankAccountStatus, type BankStatementSummary } from "../../src/api/client";
import { startEarly } from "../../src/api/early";
import { hasAnyRole, requireWorkbenchUser } from "../../src/auth/session";
import { describeBankAccount, formatMoneyZar, formatStatementDate } from "../../src/formatters.mjs";
import { ButtonLink, DashboardPage, DataTable, EmptyState, StatTile, TileGrid, type AttentionItem, type Column } from "../../src/ui";

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

/** The banking overview: each account, and the statements recently created in Acumatica. */
export default async function BankingPage() {
  const statusCall = startEarly(getBankingStatus());
  const user = await requireWorkbenchUser("/banking");
  const canImport = hasAnyRole(user, ["Admin", "Operator"]);

  // The page still renders when Acumatica cannot be read, so the person sees why.
  let accounts: BankAccountStatus[] = [];
  let statusFailed = false;
  try {
    accounts = (await statusCall).accounts;
  } catch {
    statusFailed = true;
  }

  const staleAccounts = accounts.filter((account) => account.stale).map((account) => account.cashAccount);
  const attentionItems: AttentionItem[] = [
    ...(statusFailed
      ? [
          {
            id: "acumatica",
            status: "Failed",
            title: "Acumatica could not be reached",
            detail: "The dates below may be out of date. Try again in a few minutes before you import.",
          },
        ]
      : []),
    ...(staleAccounts.length > 0
      ? [
          {
            id: "stale",
            status: "Late",
            title: `Acumatica could not be read just now for ${staleAccounts.join(", ")}`,
            detail: "Those accounts show the dates from the last good read. Check again before you import.",
          },
        ]
      : []),
  ];

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
        items: attentionItems,
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
                detail={describeBankAccount(account)}
                tone={account.available && !account.stale ? "default" : "warning"}
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
