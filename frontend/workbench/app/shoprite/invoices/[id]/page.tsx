import Link from "next/link";
import { notFound } from "next/navigation";
import { submitInvoiceAction } from "../../../actions";
import { getInvoiceCandidate, type InvoiceCandidateDetail, type InvoiceSubmissionAttempt, type ValidationIssue } from "../../../../src/api/client";
import { hasAnyRole, requireWorkbenchUser } from "../../../../src/auth/session";
import { formatDateTime, formatMoneyZar } from "../../../../src/formatters.mjs";
import {
  CodeBlock,
  ConfirmDialog,
  DataTable,
  EmptyState,
  KeyValueList,
  Notice,
  Panel,
  RecordPage,
  StatusBadge,
  TabNav,
  Timeline,
  type Column,
} from "../../../../src/ui";

export const dynamic = "force-dynamic";
export const metadata = { title: "Shoprite invoice" };

type InvoiceDetailPageProps = {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ submissionError?: string; submissionStatus?: string; tab?: string }>;
};

type InvoiceLine = NonNullable<InvoiceCandidateDetail["canonicalInvoice"]>["lines"][number];

const lineColumns: Column<InvoiceLine>[] = [
  { key: "line", header: "Line", render: (line) => line.lineNumber, align: "end" },
  { key: "item", header: "Item", render: (line) => line.acumaticaInventoryId, mono: true },
  { key: "description", header: "Description", render: (line) => line.description },
  { key: "gtin", header: "GTIN", render: (line) => line.gtin ?? "Missing", mono: true },
  { key: "quantity", header: "Quantity", render: (line) => `${line.quantity} ${line.acumaticaUom}`, align: "end" },
  { key: "uom", header: "Shoprite unit", render: (line) => line.shopriteUom ?? "Missing", mono: true },
  {
    key: "status",
    header: "Mapping",
    render: (line) =>
      line.gtin && line.shopriteUom && line.isShopriteUomVerified ? (
        <StatusBadge status="Ready" label="Configured" />
      ) : (
        <StatusBadge status="NeedsReview" label={line.shopriteUom && !line.isShopriteUomVerified ? "Unit not verified" : "Needs mapping"} />
      ),
  },
];

const issueColumns: Column<ValidationIssue>[] = [
  { key: "severity", header: "Severity", render: (issue) => <StatusBadge status={issue.severity === "Blocking" ? "Blocked" : "Warning"} label={issue.severity} /> },
  { key: "message", header: "Problem", render: (issue) => issue.message },
  { key: "fix", header: "Where to fix it", render: (issue) => issue.fixLocation },
  { key: "code", header: "Code", render: (issue) => issue.code, mono: true, weak: true },
];

const attemptColumns: Column<InvoiceSubmissionAttempt>[] = [
  { key: "created", header: "Sent", render: (attempt) => formatDateTime(attempt.createdAt) },
  { key: "status", header: "Result", render: (attempt) => <StatusBadge status={attempt.status} /> },
  { key: "mode", header: "How", render: (attempt) => attempt.initiationMode },
  { key: "user", header: "By", render: (attempt) => attempt.initiatedBy },
  { key: "http", header: "HTTP", render: (attempt) => attempt.responseStatusCode ?? "—", align: "end" },
  { key: "detail", header: "Detail", render: (attempt) => attempt.failureClassification ?? attempt.errorMessage ?? "—", weak: true },
];

export default async function InvoiceDetailPage({ params, searchParams }: InvoiceDetailPageProps) {
  const { id } = await params;
  const query = await searchParams;
  const user = await requireWorkbenchUser(`/shoprite/invoices/${id}`);
  const candidate = await loadCandidate(id);
  const invoice = candidate.canonicalInvoice;
  const order = candidate.matchedPurchaseOrder;
  const canWrite = hasAnyRole(user, ["Admin", "Operator"]);
  const isAdmin = hasAnyRole(user, ["Admin"]);
  const blocking = candidate.validation.issues.filter((issue) => issue.severity === "Blocking");
  const tab = query.tab === "xml" || query.tab === "attempts" ? query.tab : "lines";
  const base = `/shoprite/invoices/${id}`;
  const number = invoice?.invoiceNumber ?? "Invoice";

  return (
    <RecordPage
      back={{ href: "/shoprite/invoices", label: "Shoprite invoices" }}
      header={{
        eyebrow: "Shoprite invoice",
        title: number,
        description: [
          order?.deliveryLocationName ?? invoice?.customerLocation ?? "Store or DC not mapped",
          invoice ? formatMoneyZar(invoice.totalIncludingTax.amount) + " incl. VAT" : null,
        ]
          .filter(Boolean)
          .join(" · "),
        actions: (
          <>
            <StatusBadge status={candidate.status} />
            {candidate.canSubmit && canWrite ? (
              <ConfirmDialog
                triggerLabel="Submit to Shoprite"
                triggerVariant="primary"
                title={`Submit ${number} to Shoprite?`}
                confirmLabel="Submit to Shoprite"
                pendingLabel="Submitting…"
                action={submitInvoiceAction}
                fields={{ id: candidate.id }}
              >
                <p>
                  Shoprite has no duplicate protection. A second submission creates a second invoice on their side.
                </p>
              </ConfirmDialog>
            ) : null}
          </>
        ),
      }}
      notice={
        <>
          {query.submissionError ? (
            <Notice tone="error" title="Invoice not submitted" live>
              {query.submissionError}
            </Notice>
          ) : null}
          {query.submissionStatus ? (
            <Notice tone="success" title="Submission finished" live>
              {query.submissionStatus}
            </Notice>
          ) : null}
          {!candidate.canSubmit && blocking.length > 0 ? (
            <Notice tone="warning" title={blocking.length === 1 ? "1 problem blocks submission" : `${blocking.length} problems block submission`}>
              <p>
                {blocking.map((issue) => `${issue.message} Fix it in: ${issue.fixLocation}.`).join(" ")} The invoice is
                checked again on the next read.
              </p>
            </Notice>
          ) : null}
        </>
      }
      summary={
        <KeyValueList
          items={[
            { label: "Acumatica ID", value: invoice?.acumaticaInvoiceId, mono: true },
            { label: "Customer", value: invoice?.customerAccount, mono: true },
            {
              label: "Shoprite PO",
              value: order ? (
                <Link href={`/shoprite/purchase-orders/${order.id}`}>{order.purchaseOrderNumber}</Link>
              ) : (
                invoice?.shopritePurchaseOrderNumber
              ),
              mono: true,
            },
            {
              label: "Deliver to",
              value: order ? [order.deliveryLocationCode, order.deliveryLocationName].filter(Boolean).join(" · ") : invoice?.customerLocation,
            },
            { label: "Store or DC GLN", value: invoice?.storeDcGln, mono: true },
            { label: "Supplier GLN", value: invoice?.supplierGln, mono: true },
            { label: "Excluding VAT", value: invoice ? formatMoneyZar(invoice.totalExcludingTax.amount) : null },
            { label: "VAT", value: invoice ? formatMoneyZar(invoice.totalTax.amount) : null },
            { label: "Including VAT", value: invoice ? formatMoneyZar(invoice.totalIncludingTax.amount) : null },
          ]}
        />
      }
      activity={
        candidate.attempts.length > 0 ? (
          <Timeline
            label="Submission activity"
            entries={candidate.attempts.map((attempt) => ({
              id: attempt.id,
              time: formatDateTime(attempt.createdAt),
              dateTime: attempt.createdAt,
              title: `${attempt.initiationMode} submission by ${attempt.initiatedBy}`,
              status: <StatusBadge status={attempt.status} />,
              detail: attempt.errorMessage,
            }))}
          />
        ) : (
          <EmptyState title="Not sent yet" description="Each submission to Shoprite shows here." />
        )
      }
      sections={
        <TabNav
          label="Invoice sections"
          tabs={[
            { label: "Lines and checks", href: base, current: tab === "lines", count: invoice?.lines.length },
            { label: "Submissions", href: `${base}?tab=attempts`, current: tab === "attempts", count: candidate.attempts.length },
            ...(isAdmin ? [{ label: "Generated XML", href: `${base}?tab=xml`, current: tab === "xml" }] : []),
          ]}
        />
      }
    >
      {tab === "lines" ? (
        <>
          <Panel
            flush
            title="Validation"
            description={candidate.validation.issues.length === 0 ? "No problems. The invoice is ready for Shoprite." : undefined}
          >
            {candidate.validation.issues.length > 0 ? (
              <DataTable
                caption="Validation problems"
                columns={issueColumns}
                rows={candidate.validation.issues}
                rowKey={(issue) => issue.code}
                empty={null}
              />
            ) : null}
          </Panel>
          <Panel
            flush
            title="Lines"
            description="Each line needs a GTIN and a verified Shoprite unit."
            actions={isAdmin ? <Link href="/shoprite/settings/inventory-mappings">Inventory mappings</Link> : undefined}
          >
            <DataTable
              caption="Invoice lines"
              columns={lineColumns}
              rows={invoice?.lines ?? []}
              rowKey={(line) => String(line.lineNumber)}
              empty={<EmptyState title="This invoice has no lines" />}
            />
          </Panel>
        </>
      ) : null}

      {tab === "attempts" ? (
        <Panel flush title="Submissions">
          <DataTable
            caption="Submission attempts"
            columns={attemptColumns}
            rows={candidate.attempts}
            rowKey={(attempt) => attempt.id}
            empty={<EmptyState title="Not sent yet" description="Each submission to Shoprite shows here." />}
          />
        </Panel>
      ) : null}

      {tab === "xml" && isAdmin ? (
        <Panel title="Generated XML" description="The payload the next submission sends. Read only.">
          <CodeBlock
            label="Generated invoice XML"
            code={candidate.generatedXml}
            language="xml"
            empty="No XML yet. It is generated when the invoice passes validation."
          />
        </Panel>
      ) : null}
    </RecordPage>
  );
}

async function loadCandidate(id: string) {
  try {
    return await getInvoiceCandidate(id);
  } catch {
    notFound();
  }
}
