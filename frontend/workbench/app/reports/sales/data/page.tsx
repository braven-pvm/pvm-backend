import {
  getSalesReportLines,
  getSalesReportOptions,
  type SalesReportLine,
  type SalesReportLinePage,
  type SalesReportOptions,
} from "../../../../src/api/sales-report";
import { formatMoneyZar, formatStatementDate } from "../../../../src/formatters.mjs";
import { formatFigure, reportQuery } from "../../../../src/sales-report";
import { DataTable, EmptyState, ListPage, Notice, Pagination, type Column } from "../../../../src/ui";
import { basePath, hrefWith, requireReportUser, SalesFilters, SalesSections, type Search } from "../parts";

export const dynamic = "force-dynamic";
export const metadata = { title: "Sales lines" };

const path = `${basePath}/data`;

const columns: Column<SalesReportLine>[] = [
  { key: "date", header: "Date", render: (line) => formatStatementDate(line.documentDate) },
  { key: "document", header: "Document", render: (line) => `${line.documentType === "CreditMemo" ? "CRM" : "INV"} ${line.referenceNumber}`, mono: true },
  { key: "customer", header: "Customer", render: (line) => line.customerName ?? line.customerId },
  { key: "product", header: "Product", render: (line) => line.product ?? line.inventoryId },
  { key: "rep", header: "Rep", render: (line) => line.rep ?? "No rep", weak: true },
  { key: "quantity", header: "Quantity", render: (line) => formatFigure(line.quantity), align: "end" },
  { key: "amount", header: "Amount", render: (line) => formatMoneyZar(line.amount), align: "end" },
];

/** The sales lines behind the report, 100 for each page. */
export default async function SalesDataPage({ searchParams }: { searchParams: Promise<Search> }) {
  await requireReportUser(path);
  const search = await searchParams;
  const page = Math.max(1, Number.parseInt(search.page ?? "1", 10) || 1);

  let lines: SalesReportLinePage | undefined;
  let message: string | undefined;
  let options: SalesReportOptions = { reps: [], provinces: [], itemGroups: [], channels: [] };
  let failed = false;
  try {
    let answer;
    [answer, options] = await Promise.all([getSalesReportLines(reportQuery(search), page), getSalesReportOptions()]);
    ({ data: lines, message } = answer);
  } catch {
    failed = true;
  }

  const from = lines && lines.totalLines > 0 ? (lines.page - 1) * lines.pageSize + 1 : 0;
  const to = lines ? Math.min(lines.totalLines, lines.page * lines.pageSize) : 0;

  return (
    <ListPage
      header={{
        eyebrow: "Sales report",
        title: "Sales lines",
        description: "The invoice and credit memo lines that the report counts, in the order of the financial months.",
      }}
      sections={<SalesSections current={path} search={search} />}
      notice={
        failed ? (
          <Notice tone="error" title="The sales lines could not be read">
            Try again in a few minutes.
          </Notice>
        ) : message ? (
          <Notice tone="warning" title="The filters are not correct">
            {message}
          </Notice>
        ) : undefined
      }
      filters={<SalesFilters path={path} search={search} options={options} />}
      table={
        lines ? (
          <DataTable
            caption="Sales lines"
            columns={columns}
            rows={lines.lines}
            rowKey={(line) => `${line.documentType}-${line.referenceNumber}-${line.inventoryId}-${line.quantity}-${line.amount}`}
            empty={<EmptyState title="No sales lines for these filters" description="Choose other financial years, or clear the filters." />}
          />
        ) : null
      }
      pagination={
        lines && lines.totalLines > 0 ? (
          <Pagination
            from={from}
            to={to}
            total={lines.totalLines}
            previousHref={page > 1 ? hrefWith(path, search, { page: String(page - 1) }) : undefined}
            nextHref={to < lines.totalLines ? hrefWith(path, search, { page: String(page + 1) }) : undefined}
          />
        ) : undefined
      }
    />
  );
}
