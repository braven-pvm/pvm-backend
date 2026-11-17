import { getReviewList, type ReviewListItem } from "../../../src/api/documents";
import { hasAnyRole, requireWorkbenchUser } from "../../../src/auth/session";
import { documentTypeLabel, formatReviewReason } from "../../../src/documents";
import { formatDateTime } from "../../../src/formatters.mjs";
import { ButtonLink, DataTable, EmptyState, ListPage, Notice, Pagination, type Column } from "../../../src/ui";
import { DocumentStateBadge } from "../document-parts";

export const dynamic = "force-dynamic";
export const metadata = { title: "Review list" };

// The state and the reason come first: on a phone the table scrolls sideways.
const columns: Column<ReviewListItem>[] = [
  { key: "uploaded", header: "Uploaded", render: (row) => formatDateTime(row.uploadedAt) },
  { key: "state", header: "State", render: (row) => <DocumentStateBadge state={row.state} stuck={row.stuck} /> },
  {
    key: "reason",
    header: "Why it is here",
    render: (row) =>
      row.stuck
        ? "Information only. Nothing to file yet."
        : (formatReviewReason(row.reviewReason, row.state) ?? (row.state === "Failed" ? "Processing failed after every retry" : "—")),
  },
  { key: "by", header: "Uploaded by", render: (row) => row.uploadedBy, weak: true },
  {
    key: "document",
    header: "Document",
    render: (row) => `${documentTypeLabel(row.documentType)}, ${row.pageCount === 1 ? "1 page" : `${row.pageCount} pages`}`,
    weak: true,
  },
];

export default async function ReviewListPage({ searchParams }: { searchParams: Promise<{ page?: string }> }) {
  const user = await requireWorkbenchUser("/documents/review");
  const canAct = hasAnyRole(user, ["Admin", "Operator"]);
  const params = await searchParams;
  const page = Math.max(Number(params.page) || 1, 1);
  const result = await getReviewList(page);
  const stuck = result.items.filter((item) => item.stuck).length;

  const first = (result.page - 1) * result.pageSize + 1;
  const last = first + result.items.length - 1;
  const pageHref = (target: number) => (target > 1 ? `/documents/review?page=${target}` : "/documents/review");

  return (
    <ListPage
      header={{
        eyebrow: "Documents",
        title: "Review list",
        description: `Documents the backend could not file on its own, oldest first. ${result.total === 1 ? "1 document" : `${result.total} documents`} open.`,
      }}
      notice={
        <>
          {stuck > 0 ? (
            <Notice tone="warning" title={stuck === 1 ? "1 document is stuck" : `${stuck} documents are stuck`}>
              Processing did not finish within 30 minutes. These rows are for information: there is nothing to file or
              reject. Tell the backoffice developer if they stay here.
            </Notice>
          ) : null}
          {!canAct ? (
            <Notice tone="info" title="Only an Admin or Operator can file or reject documents">
              You can open each document and see what was read.
            </Notice>
          ) : null}
        </>
      }
      table={
        <DataTable
          caption="Documents to review"
          columns={columns}
          rows={result.items}
          rowKey={(row) => row.id}
          rowHref={(row) => `/documents/${row.id}`}
          empty={
            <EmptyState
              title="Nothing to review"
              description="Documents show here when the backend cannot match them to one invoice."
              action={<ButtonLink href="/documents">Search documents</ButtonLink>}
            />
          }
        />
      }
      pagination={
        result.items.length > 0 ? (
          <Pagination
            from={first}
            to={last}
            total={result.total}
            previousHref={result.page > 1 ? pageHref(result.page - 1) : undefined}
            nextHref={last < result.total ? pageHref(result.page + 1) : undefined}
          />
        ) : null
      }
    />
  );
}
