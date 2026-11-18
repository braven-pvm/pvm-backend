import { redirect } from "next/navigation";
import { getStatement, getStatementCompanies, type MerchandiserStatement, type StatementCompanySummary } from "../../src/api/statements";
import { hasAnyRole, requireWorkbenchUser } from "../../src/auth/session";
import { formatMoneyZar } from "../../src/formatters.mjs";
import { describeMonths } from "../../src/sell-out.mjs";
import {
  Button,
  ButtonLink,
  DataTable,
  EmptyState,
  FormActions,
  KeyValueList,
  Notice,
  PageFrame,
  PageHeader,
  Panel,
  SelectField,
  Stack,
  StatTile,
  TextField,
  TileGrid,
  type Column,
} from "../../src/ui";

export const dynamic = "force-dynamic";
export const metadata = { title: "Merchandiser statements" };

type Search = { company?: string; from?: string; to?: string };

/** The month before this one, as 2026-10: the usual statement month. */
function lastMonth() {
  const now = new Date();
  const month = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - 1, 1));
  return month.toISOString().slice(0, 7);
}

const customerColumns: Column<{ customerId: string; name: string; amount: number }>[] = [
  { key: "name", header: "Customer", render: (row) => row.name },
  { key: "amount", header: "Amount", render: (row) => formatMoneyZar(row.amount), align: "end" },
];

export default async function StatementsPage({ searchParams }: { searchParams: Promise<Search> }) {
  const user = await requireWorkbenchUser("/statements");
  if (!hasAnyRole(user, ["Admin", "Operator"])) {
    redirect("/");
  }

  const search = await searchParams;
  let companies: StatementCompanySummary[] = [];
  let failed = false;
  try {
    companies = await getStatementCompanies();
  } catch {
    failed = true;
  }

  const names = [...new Set(companies.map((company) => company.name))];
  const company = search.company ?? names[0];
  const from = search.from ?? lastMonth();
  const to = search.to || undefined;

  let statement: MerchandiserStatement | undefined;
  let message: string | undefined;
  if (company && !failed) {
    try {
      ({ statement, message } = await getStatement(company, from, to));
    } catch {
      failed = true;
    }
  }

  return (
    <PageFrame>
      <PageHeader
        eyebrow="Reports"
        title="Merchandiser statements"
        description="The monthly statement of each merchandiser company: the sales in its area, its Makro stores, and its commission or retainer."
        actions={
          <ButtonLink href="/statements/settings" variant="secondary">
            Settings
          </ButtonLink>
        }
      />
      <Stack gap="m">
        <Panel title="Choose the statement">
          <form method="get">
            <Stack gap="s">
              <SelectField name="company" label="Company" options={names.map((name) => ({ value: name, label: name }))} defaultValue={company} width="m" />
              <TextField name="from" label="Month" hint="As 2026-10." defaultValue={from} width="s" />
              <TextField name="to" label="Last month" hint="Only for a statement of several months, such as Alchemy." defaultValue={to ?? ""} width="s" />
              <FormActions>
                <Button variant="primary" type="submit">
                  Show the statement
                </Button>
              </FormActions>
            </Stack>
          </form>
        </Panel>

        {failed ? (
          <Notice tone="error" title="The statement could not be read">
            Try again in a few minutes.
          </Notice>
        ) : null}
        {!failed && names.length === 0 ? (
          <EmptyState title="No merchandiser companies yet" description="Statements show here when the settings of a company are entered." />
        ) : null}
        {message ? (
          <Notice tone="warning" title="No statement">
            {message}
          </Notice>
        ) : null}
        {statement ? (
          <FormActions>
            <ButtonLink href={`/statements/export?${new URLSearchParams({ company: company!, from, ...(to ? { to } : {}) })}`} variant="primary">
              Download as Excel
            </ButtonLink>
          </FormActions>
        ) : null}
        {statement ? <Statement statement={statement} /> : null}
      </Stack>
    </PageFrame>
  );
}

function Statement({ statement }: { statement: MerchandiserStatement }) {
  return (
    <Stack gap="m">
      {statement.makroIsPartial ? (
        <Notice tone="warning" title="The Makro month is not complete">
          The latest Makro file was made before the end of this month. Load a later file before you send the statement.
        </Notice>
      ) : null}

      <TileGrid min={200}>
        <StatTile label="Grand total" value={formatMoneyZar(statement.grandTotal)} detail={describeMonths([statement.firstMonth, statement.lastMonth])} />
        {statement.commission != null ? (
          <StatTile label={`Commission ${statement.ratePercent}%`} value={formatMoneyZar(statement.commission)} />
        ) : null}
        {statement.retainerAmount != null ? <StatTile label="Retainer" value={formatMoneyZar(statement.retainerAmount)} /> : null}
      </TileGrid>

      {statement.areas.map((area) => (
        <Panel key={area.name} title={area.name} description={`Total ${formatMoneyZar(area.total)}${area.commission != null ? ` · commission ${area.commissionRatePercent}% ${formatMoneyZar(area.commission)}` : ""}`}>
          <Stack gap="m">
            <KeyValueList
              items={[
                ...area.lines.map((line) => ({ label: line.label, value: formatMoneyZar(line.amount) })),
                ...(area.makroStores.length > 0 ? [{ label: "Makro", value: formatMoneyZar(area.makroValue) }] : []),
              ]}
            />
            {area.lines.map((line) => (
              <details key={line.label}>
                <summary>
                  {line.label}: {line.customers.length} {line.customers.length === 1 ? "customer" : "customers"}
                </summary>
                <DataTable caption={`Customers of ${line.label}`} columns={customerColumns} rows={line.customers} rowKey={(row) => row.customerId} empty={<EmptyState title="No sales" />} />
              </details>
            ))}
            {area.makroStores.length > 0 ? (
              <details>
                <summary>
                  Makro: {area.makroStores.length} {area.makroStores.length === 1 ? "store" : "stores"}
                </summary>
                <KeyValueList items={area.makroStores.map((store) => ({ label: store.name, value: formatMoneyZar(store.value) }))} />
              </details>
            ) : null}
          </Stack>
        </Panel>
      ))}

      {statement.disChem ? (
        <Panel flush title="Dis-Chem: this year and the year before" description={describeMonths([statement.disChem.months[0], statement.disChem.months[statement.disChem.months.length - 1]])}>
          <DataTable
            caption="Dis-Chem sell-in for each store"
            columns={[
              { key: "name", header: "Store", render: (row) => row.name },
              { key: "this", header: "This year", render: (row) => formatMoneyZar(row.thisYear.reduce((a, b) => a + b, 0)), align: "end" },
              { key: "last", header: "Year before", render: (row) => formatMoneyZar(row.lastYear.reduce((a, b) => a + b, 0)), align: "end" },
              { key: "growth", header: "Growth", render: (row) => (row.growthPercent == null ? "—" : `${row.growthPercent}%`), align: "end" },
            ]}
            rows={statement.disChem.stores}
            rowKey={(row) => row.name}
            empty={<EmptyState title="No Dis-Chem sales" />}
          />
        </Panel>
      ) : null}
    </Stack>
  );
}
