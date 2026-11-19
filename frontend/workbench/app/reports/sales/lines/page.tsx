import { getLines, type SalesLine, type SalesLinePage } from "../../../../src/api/sales-report";
import { formatMoneyZar, formatStatementDate } from "../../../../src/formatters.mjs";
import { formatFigure, monthLabel, readState, selectionCount, stateQuery } from "../../../../src/sales-report";
import { ButtonLink, DataTable, EmptyState, ListPage, Notice, Pagination, type Column } from "../../../../src/ui";
import { basePath, requireReportUser, single, type Search } from "../parts";

export const dynamic = "force-dynamic";
export const metadata = { title: "Sales lines" };

const path = `${basePath}/lines`;

const columns: Column<SalesLine>[] = [
  { key: "date", header: "Date", render: (line) => formatStatementDate(line.documentDate) },
  { key: "document", header: "Document", render: (line) => `${line.documentType === "CreditMemo" ? "CRM" : "INV"} ${line.referenceNumber}`, mono: true },
  { key: "customer", header: "Customer", render: (line) => line.customerName ?? line.customerId },
  { key: "product", header: "Product", render: (line) => line.product ?? line.inventoryId },
  { key: "rep", header: "Rep", render: (line) => line.rep ?? "No rep", weak: true },
  { key: "quantity", header: "Quantity", render: (line) => formatFigure(line.quantity), align: "end" },
  { key: "amount", header: "Amount", render: (line) => formatMoneyZar(line.amount), align: "end" },
];

/** The sales lines of the filter of the report, 100 for each page. */
export default async function SalesLinesPage({ searchParams }: { searchParams: Promise<Search> }) {
  await requireReportUser(path);
  const search = await searchParams;
  const state = readState(search);
  const query = stateQuery(state, false);
  const page = Math.max(1, Number.parseInt(single(search.page) ?? "1", 10) || 1);

  let lines: SalesLinePage | undefined;
  let message: string | undefined;
  let failed = false;
  try {
    ({ data: lines, message } = await getLines(query, page));
  } catch {
    failed = true;
  }

  const from = lines && lines.totalLines > 0 ? (lines.page - 1) * lines.pageSize + 1 : 0;
  const to = lines ? Math.min(lines.totalLines, lines.page * lines.pageSize) : 0;
  const pageHref = (number: number) => `${path}?${query}&page=${number}`;
  const filters = selectionCount(state);

  return (
    <ListPage
      header={{
        eyebrow: "Sales report",
        title: "Sales lines",
        description: `The invoice and credit memo lines of ${monthLabel(state.from)} to ${monthLabel(state.to)}${filters > 0 ? `, with ${filters} filter values` : ""}.`,
        actions: <ButtonLink href={`${basePath}?${query}`}>Back to the report</ButtonLink>,
      }}
      notice={
        failed ? (
          <Notice tone="error" title="The sales lines could not be read">
            Try again in a few minutes.
          </Notice>
        ) : message ? (
          <Notice tone="warning" title="The settings are not correct">
            {message}
          </Notice>
        ) : undefined
      }
      table={
        lines ? (
          <DataTable
            caption="Sales lines"
            columns={columns}
            rows={lines.lines}
            rowKey={(line) => `${line.documentType}-${line.referenceNumber}-${line.inventoryId}-${line.quantity}-${line.amount}`}
            empty={<EmptyState title="No sales lines for these settings" description="Choose another period, or clear some filters." />}
          />
        ) : null
      }
      pagination={
        lines && lines.totalLines > 0 ? (
          <Pagination
            from={from}
            to={to}
            total={lines.totalLines}
            previousHref={page > 1 ? pageHref(page - 1) : undefined}
            nextHref={to < lines.totalLines ? pageHref(page + 1) : undefined}
          />
        ) : undefined
      }
    />
  );
}
