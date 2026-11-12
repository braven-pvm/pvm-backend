import {
  ButtonLink,
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
import { devOnly } from "../sample";

export const metadata = { title: "Record template" };

type Line = { item: string; description: string; gtin: string; qty: string; total: string };

const lines: Line[] = [
  { item: "ENER12", description: "Energade lemon 500 ml", gtin: "6001007611212", qty: "12 cases", total: "R 6 912.00" },
  { item: "ENER10", description: "Energade naartjie 500 ml", gtin: "6001007611014", qty: "10 cases", total: "R 5 760.00" },
  { item: "ENER3", description: "Energade mixed berry 500 ml", gtin: "6001007611304", qty: "4 cases", total: "R 2 304.00" },
  { item: "DYNA31", description: "Octane orange 1 kg", gtin: "6009880461317", qty: "8 units", total: "R 1 043.48" },
];

const lineColumns: Column<Line>[] = [
  { key: "item", header: "Item", render: (row) => row.item, mono: true },
  { key: "description", header: "Description", render: (row) => row.description },
  { key: "gtin", header: "GTIN", render: (row) => row.gtin, mono: true },
  { key: "qty", header: "Quantity", render: (row) => row.qty, align: "end" },
  { key: "total", header: "Line total", render: (row) => row.total, align: "end" },
];

export default function RecordTemplateExample() {
  devOnly();
  const base = "/ui-kit/templates/record";

  return (
    <RecordPage
      back={{ href: "/ui-kit/templates/list", label: "Shoprite invoices" }}
      header={{
        eyebrow: "Shoprite invoice",
        title: "INV190970",
        description: "Shoprite Centurion DC · 10 Nov 2026 · R 18 422.40 incl. VAT",
        actions: (
          <>
            <StatusBadge status="NeedsReview" />
            {/* An invoice that needs review cannot be sent, so the main action leads to the fix. */}
            <ButtonLink href="/ui-kit/templates/record" variant="primary">
              Open in Acumatica
            </ButtonLink>
          </>
        ),
      }}
      notice={
        <Notice tone="warning" title="Shoprite PO number is missing">
          Add the customer order number on the Acumatica sales order. This invoice checks itself again on the next sync, every 10 minutes.
        </Notice>
      }
      summary={
        <KeyValueList
          items={[
            { label: "Customer", value: "DEB2062 · Shoprite Checkers" },
            { label: "Deliver to", value: "Shoprite Centurion DC" },
            { label: "GLN", value: "6001007000207", mono: true },
            { label: "Shoprite PO", value: null },
            { label: "Lines", value: "4" },
            { label: "VAT", value: "R 2 402.92" },
          ]}
        />
      }
      activity={
        <Timeline
          label="Invoice activity"
          entries={[
            { id: "3", time: "10 Nov, 14:12", title: "Validation failed: no Shoprite PO number", status: <StatusBadge status="NeedsReview" /> },
            { id: "2", time: "10 Nov, 14:12", title: "Read from Acumatica by invoice reconciliation" },
            { id: "1", time: "10 Nov, 13:58", title: "Invoice finalised in Acumatica" },
          ]}
        />
      }
      sections={
        <TabNav
          label="Invoice sections"
          tabs={[
            { label: "Lines", href: base, current: true, count: 4 },
            { label: "Raw payload", href: `${base}?tab=raw`, current: false },
          ]}
        />
      }
    >
      <Panel flush>
        <DataTable
          caption="Invoice lines"
          columns={lineColumns}
          rows={lines}
          rowKey={(row) => row.item}
          empty={<EmptyState title="This invoice has no lines" />}
        />
      </Panel>
    </RecordPage>
  );
}
